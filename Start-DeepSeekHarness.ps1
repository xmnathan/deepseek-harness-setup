[CmdletBinding()]
param(
    [string]$InstallDir = '',
    [string]$ConfigPath = ''
)

$ErrorActionPreference = 'Stop'

if (-not $InstallDir) {
    if ($ConfigPath) {
        $InstallDir = Split-Path -Parent $ConfigPath
    } else {
        $InstallDir = Join-Path $env:LOCALAPPDATA 'DeepSeekHarness'
    }
}

$appRoot = [IO.Path]::GetFullPath($InstallDir)
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $appRoot 'config.json'
}

$logRoot = Join-Path $appRoot 'logs'
$npmCacheRoot = Join-Path $appRoot 'npm-cache'
New-Item -ItemType Directory -Force -Path $appRoot, $logRoot, $npmCacheRoot | Out-Null
$env:npm_config_cache = $npmCacheRoot

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

function Get-NpxPath {
    Refresh-NodePath

    $command = Get-Command npx.cmd -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(Join-Path $env:ProgramFiles 'nodejs\npx.cmd')
    if (${env:ProgramFiles(x86)}) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} 'nodejs\npx.cmd')
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'npx.cmd was not found. Install Node.js LTS first.'
}

$packageName = '@deepseek-ai/dsh@latest'
$arguments = @('web')
$webUrl = 'http://127.0.0.1:3080'

if (Test-Path -LiteralPath $ConfigPath) {
    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    if ($config.PackageName) {
        $packageName = [string]$config.PackageName
    }
    if ($config.Arguments) {
        $arguments = @($config.Arguments | ForEach-Object { [string]$_ })
    }
    if ($config.Url) {
        $webUrl = [string]$config.Url
    }
}

$npx = Get-NpxPath
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logFile = Join-Path $logRoot "deepseek-harness-$timestamp.log"
$latestLog = Join-Path $logRoot 'latest.log'

"[$(Get-Date -Format s)] Starting DeepSeek Harness" | Tee-Object -FilePath $logFile | Out-Null
"installDir: $appRoot" | Tee-Object -FilePath $logFile -Append | Out-Null
"npmCache: $npmCacheRoot" | Tee-Object -FilePath $logFile -Append | Out-Null
"npx: $npx" | Tee-Object -FilePath $logFile -Append | Out-Null
"package: $packageName" | Tee-Object -FilePath $logFile -Append | Out-Null
"arguments: $($arguments -join ' ')" | Tee-Object -FilePath $logFile -Append | Out-Null
"url: $webUrl" | Tee-Object -FilePath $logFile -Append | Out-Null

Copy-Item -LiteralPath $logFile -Destination $latestLog -Force

$npxArguments = @('--yes', $packageName) + $arguments
& $npx @npxArguments 2>&1 | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog
$exitCode = $LASTEXITCODE

"[$(Get-Date -Format s)] Exited with code $exitCode" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
exit $exitCode
