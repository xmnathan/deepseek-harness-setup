[CmdletBinding()]
param(
    [string]$SourceHome = (Join-Path $env:USERPROFILE '.dsh'),
    [string]$TargetHome = 'D:\deepseek-harness\home',
    [string]$SourceDir = 'D:\deepseek-harness',
    [string]$BackupRoot = 'D:\deepseek-harness\migration-backups',
    [switch]$StopRunning,
    [switch]$KeepGeneratedProfileDeps,
    [switch]$SkipProfileInstall
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    [Console]::OutputEncoding = $OutputEncoding
} catch {
}

function Invoke-Robocopy {
    param(
        [string]$Source,
        [string]$Target,
        [string[]]$ExtraArgs = @()
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        Write-Host "Skip missing source: $Source"
        return
    }

    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    $args = @(
        $Source,
        $Target,
        '/E',
        '/COPY:DAT',
        '/DCOPY:DAT',
        '/R:2',
        '/W:1',
        '/NP',
        '/NFL',
        '/NDL'
    ) + $ExtraArgs

    Write-Host "Robocopy: $Source -> $Target"
    & robocopy.exe @args
    $code = $LASTEXITCODE
    if ($code -gt 7) {
        throw "robocopy failed with code $code"
    }
}

function Remove-GeneratedProfileDeps {
    param(
        [string]$DshHome
    )

    $profiles = Join-Path $DshHome 'profiles'
    if (-not (Test-Path -LiteralPath $profiles)) {
        return
    }

    $generatedDirs = @()
    $rootNodeModules = Join-Path $profiles 'node_modules'
    if (Test-Path -LiteralPath $rootNodeModules) {
        $generatedDirs += $rootNodeModules
    }

    Get-ChildItem -LiteralPath $profiles -Directory -Force | ForEach-Object {
        $profileNodeModules = Join-Path $_.FullName 'node_modules'
        $fallback = Join-Path $_.FullName '.dsh-module-fallback'
        if (Test-Path -LiteralPath $profileNodeModules) {
            $generatedDirs += $profileNodeModules
        }
        if (Test-Path -LiteralPath $fallback) {
            $generatedDirs += $fallback
        }
    }

    foreach ($dir in ($generatedDirs | Select-Object -Unique)) {
        Write-Host "Removing generated profile dependency directory: $dir"
        Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop
    }
}

function Add-ExistingPath {
    param([string]$Path)

    if ($Path -and (Test-Path -LiteralPath $Path) -and (($env:Path -split ';') -notcontains $Path)) {
        $env:Path = "$Path;$env:Path"
    }
}

function Refresh-NodePath {
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = @($machinePath, $userPath, $env:Path) -join ';'

    Add-ExistingPath (Join-Path $env:ProgramFiles 'nodejs')
    if (${env:ProgramFiles(x86)}) {
        Add-ExistingPath (Join-Path ${env:ProgramFiles(x86)} 'nodejs')
    }
}

function Get-CorepackPath {
    Refresh-NodePath

    $command = Get-Command corepack.cmd -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(Join-Path $env:ProgramFiles 'nodejs\corepack.cmd')
    if (${env:ProgramFiles(x86)}) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} 'nodejs\corepack.cmd')
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Install-ProfileDependencies {
    param(
        [string]$SourceRoot,
        [string]$DshHome
    )

    if (-not (Test-Path -LiteralPath (Join-Path $SourceRoot 'package.json'))) {
        Write-Host "Skip profile dependency install; source directory is not available: $SourceRoot"
        return @()
    }

    $profiles = Join-Path $DshHome 'profiles'
    if (-not (Test-Path -LiteralPath $profiles)) {
        Write-Host 'Skip profile dependency install; no profiles directory.'
        return @()
    }

    $corepack = Get-CorepackPath
    if (-not $corepack) {
        Write-Host 'Skip profile dependency install; corepack.cmd was not found.'
        return @()
    }

    $installedProfiles = @()
    $env:DSH_HOME = $DshHome
    $env:npm_config_cache = Join-Path (Split-Path -Parent $DshHome) 'npm-cache'
    $env:NO_COLOR = '1'
    $env:FORCE_COLOR = '0'
    $env:COREPACK_ENABLE_DOWNLOAD_PROMPT = '0'

    Push-Location $SourceRoot
    try {
        Get-ChildItem -LiteralPath $profiles -Directory -Force | ForEach-Object {
            if (-not (Test-Path -LiteralPath (Join-Path $_.FullName 'package.json'))) {
                return
            }

            $profileName = $_.Name
            Write-Host "Installing profile dependencies: $profileName"
            & $corepack pnpm run dsh plugin --profile $profileName install 2>&1 | ForEach-Object {
                Write-Host $_
            }
            if ($LASTEXITCODE -ne 0) {
                throw "profile dependency install failed for $profileName with code $LASTEXITCODE"
            }
            $installedProfiles += $profileName
        }
    } finally {
        Pop-Location
    }

    return $installedProfiles
}

function Stop-DshProcess {
    Write-Host 'Stopping DeepSeekHarness task if it exists...'
    & schtasks.exe /End /TN 'DeepSeekHarness' | Out-Host

    Write-Host 'Stopping process listening on 127.0.0.1:3080 if any...'
    $listeners = & netstat.exe -ano -p tcp |
        Select-String -Pattern ':3080\s+.*LISTENING' |
        ForEach-Object {
            ($_ -split '\s+')[-1]
        } |
        Where-Object { $_ -match '^\d+$' } |
        Select-Object -Unique

    foreach ($pidText in $listeners) {
        try {
            $process = Get-Process -Id ([int]$pidText) -ErrorAction Stop
            Write-Host "Stopping PID $pidText ($($process.ProcessName))"
            Stop-Process -Id ([int]$pidText) -Force
        } catch {
            Write-Host "Skip PID ${pidText}: $($_.Exception.Message)"
        }
    }
}

function Write-Report {
    param(
        [string]$Path,
        [object]$Report
    )

    $Report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
}

$SourceHome = [IO.Path]::GetFullPath($SourceHome)
$TargetHome = [IO.Path]::GetFullPath($TargetHome)
$SourceDir = [IO.Path]::GetFullPath($SourceDir)
$BackupRoot = [IO.Path]::GetFullPath($BackupRoot)

if (-not (Test-Path -LiteralPath $SourceHome)) {
    throw "Source DSH_HOME does not exist: $SourceHome"
}

if ($StopRunning) {
    Stop-DshProcess
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $BackupRoot "home-before-migration-$timestamp"
New-Item -ItemType Directory -Force -Path $BackupRoot, $TargetHome | Out-Null

Write-Host "Source DSH_HOME: $SourceHome"
Write-Host "Target DSH_HOME: $TargetHome"
Write-Host "Backup: $backup"

Invoke-Robocopy $TargetHome $backup @(
    '/XD',
    'node_modules',
    '.dsh-module-fallback'
)

if (-not $KeepGeneratedProfileDeps) {
    Remove-GeneratedProfileDeps $TargetHome
}

Invoke-Robocopy $SourceHome $TargetHome @(
    '/XD',
    'node_modules',
    '.dsh-module-fallback'
)

$installedProfiles = @()
if (-not $SkipProfileInstall) {
    $installedProfiles = Install-ProfileDependencies $SourceDir $TargetHome
}

$report = [ordered]@{
    migratedAt = (Get-Date).ToString('s')
    sourceHome = $SourceHome
    targetHome = $TargetHome
    sourceDir = $SourceDir
    backup = $backup
    excludedDirectories = @('node_modules', '.dsh-module-fallback')
    removedGeneratedProfileDeps = (-not $KeepGeneratedProfileDeps)
    installedProfiles = $installedProfiles
    migratedItems = @(
        '.credentials.yaml',
        'settings.yaml',
        'AGENTS.md',
        'attachments',
        'profiles user files',
        'sessions',
        'storages'
    )
}

$reportPath = Join-Path $BackupRoot "migration-report-$timestamp.json"
Write-Report $reportPath $report
Write-Host "Migration report: $reportPath"
Write-Host 'Migration completed.'
