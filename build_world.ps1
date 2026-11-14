# Build the Viva Pinata: Trouble in Paradise APWorld.
# Run from anywhere: .\build_world.ps1
# No Python or local Archipelago checkout required.

$ErrorActionPreference = 'Stop'

$repoRoot    = $PSScriptRoot
$worldName   = 'vp_tip'
$worldDir    = Join-Path $repoRoot "world\$worldName"
$distDir     = Join-Path $repoRoot 'dist'
$outputFile  = Join-Path $distDir "$worldName.apworld"
$tempFile    = Join-Path $distDir "$worldName.apworld.tmp"
$manifestFile = Join-Path $worldDir 'archipelago.json'

foreach ($required in @((Join-Path $worldDir '__init__.py'), $manifestFile)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required APWorld file not found: $required"
    }
}

# Archipelago's APWorld container requires these fields in the packed
# manifest. Keep the source archipelago.json untouched.
$manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.game -ne 'Viva Pinata: Trouble in Paradise') {
    throw "Unexpected game in archipelago.json: $($manifest.game)"
}
$manifest | Add-Member -MemberType NoteProperty -Name 'version' -Value 7 -Force
$manifest | Add-Member -MemberType NoteProperty -Name 'compatible_version' -Value 7 -Force

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path -LiteralPath $tempFile) {
    Remove-Item -LiteralPath $tempFile -Force
}

try {
    $archive = [System.IO.Compression.ZipFile]::Open(
        $tempFile, [System.IO.Compression.ZipArchiveMode]::Create
    )
    try {
        $fileCount = 0
        foreach ($file in (Get-ChildItem -LiteralPath $worldDir -Recurse -File -Force)) {
            $relative = $file.FullName.Substring($worldDir.Length + 1).Replace('\', '/')
            if ($relative -eq 'archipelago.json') { continue }
            if ($file.Extension -in @('.pyc', '.pyo')) { continue }
            if ($file.Name -eq '.DS_Store') { continue }
            if ($relative -match '(^|/)(__pycache__|\.pytest_cache|\.mypy_cache|\.ruff_cache|\.git)(/|$)') {
                continue
            }

            $entryName = "$worldName/$relative"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
            $fileCount++
        }

        # Write a valid container manifest without modifying the checked-in file.
        $entry = $archive.CreateEntry(
            "$worldName/archipelago.json", [System.IO.Compression.CompressionLevel]::Optimal
        )
        $writer = [System.IO.StreamWriter]::new(
            $entry.Open(), [System.Text.UTF8Encoding]::new($false)
        )
        try {
            $writer.Write(($manifest | ConvertTo-Json -Depth 20))
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    if (Test-Path -LiteralPath $outputFile) {
        Remove-Item -LiteralPath $outputFile -Force
    }
    Move-Item -LiteralPath $tempFile -Destination $outputFile
    Write-Host "Built: $outputFile ($fileCount source files + manifest)" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $tempFile) {
        Remove-Item -LiteralPath $tempFile -Force
    }
}
