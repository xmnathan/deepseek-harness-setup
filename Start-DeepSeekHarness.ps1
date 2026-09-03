[CmdletBinding()]
param(
    [string]$InstallDir = '',
    [string]$ConfigPath = ''
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    [Console]::OutputEncoding = $OutputEncoding
} catch {
}

if (-not $InstallDir) {
    if ($ConfigPath) {
        $InstallDir = Split-Path -Parent $ConfigPath
    } else {
        $InstallDir = 'D:\deepseek-harness'
    }
}

$appRoot = [IO.Path]::GetFullPath($InstallDir)
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $appRoot 'config.json'
}

$logRoot = Join-Path $appRoot 'logs'
$npmCacheRoot = Join-Path $appRoot 'npm-cache'
$runtimeRoot = Join-Path $appRoot 'runtime'
$homeRoot = Join-Path $appRoot 'home'
$sourceRoot = $appRoot
New-Item -ItemType Directory -Force -Path $appRoot, $logRoot, $npmCacheRoot, $runtimeRoot, $homeRoot | Out-Null
$env:npm_config_cache = $npmCacheRoot
$env:DSH_HOME = $homeRoot
$env:NO_COLOR = '1'
$env:FORCE_COLOR = '0'
$env:COREPACK_ENABLE_DOWNLOAD_PROMPT = '0'

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

function Get-NpmPath {
    Refresh-NodePath

    $command = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = @(Join-Path $env:ProgramFiles 'nodejs\npm.cmd')
    if (${env:ProgramFiles(x86)}) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} 'nodejs\npm.cmd')
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'npm.cmd was not found. Install Node.js LTS first.'
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

function Get-PnpmPath {
    Refresh-NodePath

    $command = Get-Command pnpm.cmd -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

$packageName = '@deepseek-ai/dsh@latest'
$arguments = @('web')
$webUrl = 'http://127.0.0.1:3080'
$runMode = 'package'
$localBin = Join-Path $runtimeRoot 'node_modules\.bin\dsh.cmd'

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
    if ($config.RuntimeDir) {
        $runtimeRoot = [string]$config.RuntimeDir
    }
    if ($config.HomeDir) {
        $homeRoot = [string]$config.HomeDir
        New-Item -ItemType Directory -Force -Path $homeRoot | Out-Null
        $env:DSH_HOME = $homeRoot
    }
    if ($config.RunMode) {
        $runMode = [string]$config.RunMode
    }
    if ($config.SourceDir) {
        $sourceRoot = [string]$config.SourceDir
    }
    if ($config.LocalBin) {
        $localBin = [string]$config.LocalBin
    } else {
        $localBin = Join-Path $runtimeRoot 'node_modules\.bin\dsh.cmd'
    }
}

$npx = Get-NpxPath
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logFile = Join-Path $logRoot "deepseek-harness-$timestamp.log"
$latestLog = Join-Path $logRoot 'latest.log'

"[$(Get-Date -Format s)] Starting DeepSeek Harness" | Tee-Object -FilePath $logFile | Out-Null
"installDir: $appRoot" | Tee-Object -FilePath $logFile -Append | Out-Null
"npmCache: $npmCacheRoot" | Tee-Object -FilePath $logFile -Append | Out-Null
"dshHome: $homeRoot" | Tee-Object -FilePath $logFile -Append | Out-Null
"runMode: $runMode" | Tee-Object -FilePath $logFile -Append | Out-Null
"runtime: $runtimeRoot" | Tee-Object -FilePath $logFile -Append | Out-Null
"source: $sourceRoot" | Tee-Object -FilePath $logFile -Append | Out-Null
"localBin: $localBin" | Tee-Object -FilePath $logFile -Append | Out-Null
"npx: $npx" | Tee-Object -FilePath $logFile -Append | Out-Null
"package: $packageName" | Tee-Object -FilePath $logFile -Append | Out-Null
"arguments: $($arguments -join ' ')" | Tee-Object -FilePath $logFile -Append | Out-Null
"url: $webUrl" | Tee-Object -FilePath $logFile -Append | Out-Null

Copy-Item -LiteralPath $logFile -Destination $latestLog -Force

if ($runMode -eq 'source') {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'package.json'))) {
        throw "Source directory is not available: $sourceRoot"
    }

    $corepack = Get-CorepackPath
    $pnpm = Get-PnpmPath
    Push-Location $sourceRoot
    try {
        if ($corepack) {
            $sourceArguments = @('pnpm', 'run', 'dsh') + $arguments
            "cwd: $sourceRoot" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
            "launch: $corepack $($sourceArguments -join ' ')" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
            & $corepack @sourceArguments 2>&1 | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
        } elseif ($pnpm) {
            $sourceArguments = @('run', 'dsh') + $arguments
            "cwd: $sourceRoot" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
            "launch: $pnpm $($sourceArguments -join ' ')" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
            & $pnpm @sourceArguments 2>&1 | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
        } else {
            $npm = Get-NpmPath
            $sourceArguments = @('run', 'dsh', '--') + $arguments
            "cwd: $sourceRoot" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
            "launch: $npm $($sourceArguments -join ' ')" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
            & $npm @sourceArguments 2>&1 | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
        }
    } finally {
        Pop-Location
    }
} elseif (Test-Path -LiteralPath $localBin) {
    Push-Location $runtimeRoot
    try {
        "cwd: $runtimeRoot" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
        "launch: $localBin $($arguments -join ' ')" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
        & $localBin @arguments 2>&1 | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
    } finally {
        Pop-Location
    }
} else {
    "local bin not found, fallback to npx" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
    $npxArguments = @('--yes', $packageName) + $arguments
    & $npx @npxArguments 2>&1 | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
}
$exitCode = $LASTEXITCODE

"[$(Get-Date -Format s)] Exited with code $exitCode" | Tee-Object -FilePath $logFile -Append | Tee-Object -FilePath $latestLog -Append
exit $exitCode
