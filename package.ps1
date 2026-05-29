<#
.SYNOPSIS
    Packages an Icebreaker mod version into a cross-platform compatible ZIP file.

.DESCRIPTION
    Creates a ZIP file with forward-slash path separators (per ZIP spec APPNOTE 4.4.17)
    so the archive extracts correctly on Linux, macOS, and Windows.

.PARAMETER VersionDir
    The version directory to package (e.g. "1.22" or "1.21.6").

.PARAMETER ZipName
    Optional output ZIP filename. If not specified, uses the existing ZIP filename found
    in the version directory, or falls back to "Icebreaker.zip".

.EXAMPLE
    .\package.ps1 -VersionDir 1.22
    .\package.ps1 -VersionDir 1.21.6 -ZipName "Icebreaker_v0.2.1.zip"
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$VersionDir,

    [string]$ZipName
)

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $scriptDir $VersionDir

if (-not (Test-Path $sourceDir)) {
    Write-Error "Version directory not found: $sourceDir"
    exit 1
}

# If no ZipName specified, try to find the existing one
if (-not $ZipName) {
    $existing = Get-ChildItem -Path $sourceDir -Filter "*.zip" | Select-Object -First 1
    if ($existing) {
        $ZipName = $existing.Name
    } else {
        $ZipName = "Icebreaker.zip"
    }
}

$zipPath = Join-Path $sourceDir $ZipName

# Build output directory (from dotnet build)
$buildDir = Join-Path $sourceDir "bin\Release\Mods\mod"

if (-not (Test-Path $buildDir)) {
    Write-Host "Build output not found at $buildDir"
    Write-Host "Attempting dotnet build..."
    Push-Location $sourceDir
    dotnet build -c Release
    Pop-Location
    if (-not (Test-Path $buildDir)) {
        Write-Error "Build failed or output directory still missing: $buildDir"
        exit 1
    }
}

# Remove old ZIP if it exists
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
    Write-Host "Removed old: $ZipName"
}

# Create ZIP with forward-slash entry names for cross-platform compatibility
$zipStream = [System.IO.File]::Create($zipPath)
$archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)

$files = Get-ChildItem -Path $buildDir -Recurse -File
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($buildDir.Length + 1)
    # KEY FIX: Replace backslashes with forward slashes for ZIP spec compliance
    $entryName = $relativePath.Replace('\', '/')

    Write-Host "  Adding: $entryName"
    $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $entry.Open()
    $fileStream = [System.IO.File]::OpenRead($file.FullName)
    $fileStream.CopyTo($entryStream)
    $fileStream.Close()
    $entryStream.Close()
}

$archive.Dispose()
$zipStream.Close()

Write-Host ""
Write-Host "Packaged: $zipPath"
Write-Host "Entries:"

# Verify the result
$verify = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
foreach ($e in $verify.Entries) {
    Write-Host "  $($e.FullName)"
}
$verify.Dispose()
