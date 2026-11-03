$ErrorActionPreference = 'Stop'
$package = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.files) {
    $path = Join-Path $package $file.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package file: $($file.path)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Package checksum mismatch: $($file.path)"
    }
}
Write-Output "Verified $($manifest.files.Count) packaged files."
