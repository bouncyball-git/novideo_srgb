#Requires -Version 5.1

<#
.SYNOPSIS
    novideo_srgb minimal release build.

.DESCRIPTION
    Restores NuGet packages and builds Release|x64 with PDB and XML doc
    files stripped. The output directory is wiped before each build so
    only the runtime files (exe + 3 dependency DLLs + .config) end up
    inside it -- no stale artifacts from previous builds.

    Requires Visual Studio 2019/2022 Build Tools (MSBuild + .NET
    Framework 4.8 targeting pack).

.EXAMPLE
    .\build.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Always run from the script's own directory.
Set-Location -LiteralPath $PSScriptRoot

# --- Helper: report a missing build prerequisite and offer to open the
#     download page in the default browser. Always exits with code 1.
function Show-MissingTool {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Description,
        [Parameter(Mandatory)] [string] $Url
    )
    Write-Host ''
    Write-Host "ERROR: $Name is required but was not found." -ForegroundColor Red
    Write-Host ''
    foreach ($line in ($Description -split "`n")) {
        Write-Host "  $line" -ForegroundColor Yellow
    }
    Write-Host ''
    Write-Host "  Download page: $Url" -ForegroundColor Cyan
    Write-Host ''
    try {
        $resp = Read-Host 'Open the download page in your default browser? [Y/n]'
    } catch {
        # Non-interactive host (no console input available) -- just exit.
        $resp = 'n'
    }
    if ([string]::IsNullOrWhiteSpace($resp) -or $resp -match '^[Yy]') {
        try { Start-Process $Url | Out-Null } catch { Write-Host "Failed to open browser: $_" -ForegroundColor Red }
    }
    exit 1
}

# --- Preflight: refuse to build over a running instance ---------------------
# MSBuild can't overwrite a locked .exe; bail early with a useful message
# instead of letting the build burn 10 retries before failing.
$running = Get-Process -Name 'novideo_srgb' -ErrorAction SilentlyContinue
if ($running) {
    $procIds = ($running | ForEach-Object { $_.Id }) -join ', '
    throw "novideo_srgb.exe is currently running (PID $procIds). Close it before building (right-click the tray icon -> Exit), then re-run this script."
}

# --- Preflight: build prerequisites -----------------------------------------
# 1. vswhere.exe (ships with the VS Installer; tells us where MSBuild lives)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    Show-MissingTool `
        -Name 'Visual Studio Build Tools' `
        -Description ("vswhere.exe was not found at:`n  $vswhere`n" +
                      "This means no Visual Studio or Build Tools installation was detected on this machine.`n" +
                      "Install 'Build Tools for Visual Studio 2022' (free) and include the .NET desktop build tools workload.") `
        -Url 'https://aka.ms/vs/17/release/vs_BuildTools.exe'
}

# 2. MSBuild itself (vswhere may be present even if MSBuild component isn't)
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
    -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1

if (-not $msbuild -or -not (Test-Path -LiteralPath $msbuild)) {
    Show-MissingTool `
        -Name 'MSBuild' `
        -Description ("Visual Studio Installer is present, but no MSBuild component is installed.`n" +
                      "Run the Visual Studio Installer and add the 'MSBuild' component (part of the`n" +
                      "'.NET desktop build tools' or 'Desktop development with C++' workload).") `
        -Url 'https://aka.ms/vs/17/release/vs_BuildTools.exe'
}

# 3. .NET Framework 4.8 targeting pack (reference assemblies for the v4.8 TFM)
$targetPack = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path -LiteralPath $targetPack)) {
    Show-MissingTool `
        -Name '.NET Framework 4.8 Developer Pack' `
        -Description (".NET Framework 4.8 reference assemblies were not found at:`n  $targetPack`n" +
                      "MSBuild needs these to compile this project (TargetFrameworkVersion=v4.8).`n" +
                      "Download and install the '.NET Framework 4.8 Developer Pack' (small, ~70 MB).") `
        -Url 'https://dotnet.microsoft.com/download/dotnet-framework/net48'
}

Write-Host 'Using MSBuild:' -ForegroundColor Cyan
Write-Host "  $msbuild"
Write-Host ''

$sln    = 'novideo_srgb.sln'
$outDir = 'novideo_srgb\bin\x64\Release'

# --- Wipe previous output for a guaranteed-clean build ----------------------
# MSBuild's -t:Rebuild only cleans files it tracked itself. To make sure no
# stale .pdb / .xml / orphaned dependency from a prior build (or a different
# configuration) leaks into the output, blow the directory away entirely
# before the build runs.
if (Test-Path -LiteralPath $outDir) {
    Write-Host "=== Wiping previous output: $outDir ===" -ForegroundColor Cyan
    Remove-Item -LiteralPath $outDir -Recurse -Force
    Write-Host ''
}

# --- Restore NuGet packages -------------------------------------------------
Write-Host '=== Restoring NuGet packages ===' -ForegroundColor Cyan
& $msbuild $sln -t:Restore -p:RestorePackagesConfig=true -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed (exit $LASTEXITCODE)." }
Write-Host ''

# --- Build, stripped and optimized ------------------------------------------
# Properties used:
#   DebugType=none / DebugSymbols=false       -> no .pdb generation
#   Optimize=true                             -> Roslyn optimizer on
#   AllowedReferenceRelatedFileExtensions     -> overrides the default ".pdb;.xml"
#                                                with a non-matching value, so the
#                                                build does NOT copy any sidecar
#                                                .pdb / .xml doc files for references
Write-Host '=== Building Release|x64 [stripped, optimized] ===' -ForegroundColor Cyan
$msbuildArgs = @(
    $sln
    '-t:Rebuild'
    '-m'
    '-nologo'
    '-v:minimal'
    '-p:Configuration=Release'
    '-p:Platform=x64'
    '-p:DebugType=none'
    '-p:DebugSymbols=false'
    '-p:Optimize=true'
    '-p:AllowedReferenceRelatedFileExtensions=.allowedextensions'
)
& $msbuild @msbuildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
Write-Host ''

$exePath = Join-Path $outDir 'novideo_srgb.exe'
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Expected output file not found at: $exePath"
}

# --- Verify output is exactly what we expect (no stale files) ---------------
$expectedFiles = @(
    'novideo_srgb.exe'
    'novideo_srgb.exe.config'
    'EDIDParser.dll'
    'NvAPIWrapper.dll'
    'WindowsDisplayAPI.dll'
)
foreach ($f in $expectedFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $outDir $f))) {
        throw "Expected build artifact missing: $f"
    }
}

# Warn (but don't fail) if MSBuild ever produces extras we didn't anticipate.
$actual = Get-ChildItem -LiteralPath $outDir -File | Select-Object -ExpandProperty Name
$unexpected = $actual | Where-Object { $_ -notin $expectedFiles }
if ($unexpected) {
    Write-Host ''
    Write-Host 'WARNING: unexpected files in output directory:' -ForegroundColor Yellow
    foreach ($f in $unexpected) { Write-Host "  $f" -ForegroundColor Yellow }
}

# --- Report -----------------------------------------------------------------
Write-Host ''
Write-Host '=== Output ===' -ForegroundColor Cyan
$items = Get-ChildItem -LiteralPath $outDir -File | Sort-Object Name
$total = 0
foreach ($item in $items) {
    Write-Host ('  {0,-30} {1,12:N0} bytes' -f $item.Name, $item.Length)
    $total += $item.Length
}
Write-Host ('  {0,-30} {1,12}' -f '----------', '------------')
Write-Host ('  {0,-30} {1,12:N0} bytes' -f 'Total', $total)
Write-Host ''
Write-Host "Done. Release build is in: $((Resolve-Path -LiteralPath $outDir).Path)" -ForegroundColor Green
