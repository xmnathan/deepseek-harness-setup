[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$source = Join-Path $root 'DeepSeekHarnessSetup.cs'
$output = Join-Path $root 'DeepSeekHarnessSetup.exe'

$candidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)

$csc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw 'csc.exe was not found. Install .NET Framework 4.x developer tools or Windows SDK.'
}

& $csc `
    /nologo `
    /target:winexe `
    /platform:anycpu `
    /out:$output `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    /reference:Microsoft.CSharp.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with code $LASTEXITCODE"
}

Write-Host "Built: $output"
