
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'client\VivaAP.Launcher\VivaAP.Launcher.csproj'
$publishDir = Join-Path $repoRoot 'client\VivaAP.Launcher\obj\launcher-publish'
$distDir = Join-Path $repoRoot 'dist'
$zipPath = Join-Path $distDir 'TiP-AP-Launcher.zip'

# Clean only the temporary publish directory.
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

# Publish the independent launcher, not the native mod.
& dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "Launcher publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishDir 'TiPAP.exe'

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Expected launcher executable not found: $exePath"
}

# ZIP all publish output files, in case the SDK leaves
# any required auxiliary files alongside the executable.
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDir,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

Write-Host "Created $zipPath" -ForegroundColor Green
