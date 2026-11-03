param([Parameter(Mandatory = $true)][string]$ServerAssemblies)
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'source'
& (Join-Path $source 'build.ps1') -ServerAssemblies $ServerAssemblies
& dotnet run --project (Join-Path $source 'Tests\Checks.csproj') "-p:DuDlls=$ServerAssemblies"
if ($LASTEXITCODE -ne 0) { throw 'C# verification failed.' }
Push-Location $source
try {
    & node --test test_panel.cjs test_preview.cjs
    if ($LASTEXITCODE -ne 0) { throw 'JavaScript verification failed.' }
}
finally { Pop-Location }
Write-Output 'C# and JavaScript verification passed. Python tests: see docs/02-Development.md.'
