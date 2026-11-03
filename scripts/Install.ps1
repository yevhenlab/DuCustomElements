param(
    [Parameter(Mandatory = $true)][string]$ModsDirectory,
    [Parameter(Mandatory = $true)][string]$ServerAssemblies,
    [Parameter(Mandatory = $true)][string]$GameNetwork,
    [Parameter(Mandatory = $true)][string]$OrleansContainer,
    [switch]$Rebuild,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
$package = Split-Path $PSScriptRoot -Parent
$mods = (Resolve-Path -LiteralPath $ModsDirectory).Path
$assemblies = (Resolve-Path -LiteralPath $ServerAssemblies).Path
if (-not (Test-Path -LiteralPath $mods -PathType Container)) { throw 'ModsDirectory must be a directory.' }
& (Join-Path $PSScriptRoot 'Verify-Package.ps1')
$compatibility = Get-Content -LiteralPath (Join-Path $package 'compatibility.json') -Raw | ConvertFrom-Json
$mismatches = @($compatibility.serverAssemblies | Where-Object {
    $path = Join-Path $assemblies $_.name
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing server assembly: $path" }
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $_.sha256
})
if ($mismatches.Count -and -not $Rebuild) {
    throw 'Server assemblies differ from the tested build. Review compatibility and use -Rebuild for a new build.'
}
Write-Output "Target Mods: $mods"
Write-Output "Compiler network: $GameNetwork; Orleans container: $OrleansContainer"
Write-Output 'Existing settings and world state will be retained; updated program files will be backed up.'
if ($PlanOnly) { Write-Output 'Plan only: no files, containers or world data changed.'; return }
function Invoke-Docker([string[]]$Arguments) {
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker failed: $($Arguments -join ' ')" }
}
Invoke-Docker -Arguments @('network', 'inspect', $GameNetwork, '--format', '{{.Name}}')
$networks = & docker inspect $OrleansContainer --format '{{json .NetworkSettings.Networks}}'
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Orleans container.' }
if (-not (($networks | ConvertFrom-Json).PSObject.Properties.Name -contains $GameNetwork)) {
    throw 'Orleans must be connected to GameNetwork.'
}
$wasRunning = & docker inspect $OrleansContainer --format '{{.State.Running}}'
if ($LASTEXITCODE -ne 0) { throw 'Cannot read Orleans state.' }
$payload = Join-Path $package 'payload\Mods'
$binaryFolder = $payload
if ($Rebuild) {
    & (Join-Path $package 'source\build.ps1') -ServerAssemblies $assemblies
    $binaryFolder = Join-Path $package 'source\bin'
}
$env:CUSTOM_ELEMENTS_NETWORK = $GameNetwork
$compose = Join-Path $package 'compose.yaml'
Invoke-Docker -Arguments @('compose', '-f', $compose, 'up', '-d', '--build', '--wait', '--wait-timeout', '120')
$backup = Join-Path $mods ('custom-elements-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
if ($wasRunning -eq 'true') { Invoke-Docker -Arguments @('stop', $OrleansContainer) }
try {
    foreach ($file in Get-ChildItem -LiteralPath $payload -File) {
        $destination = Join-Path $mods $file.Name
        if ($file.Name -eq 'custom-elements.json' -and (Test-Path -LiteralPath $destination)) { continue }
        if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination $backup }
        $payloadPath = if ($file.Extension -eq '.dll') { Join-Path $binaryFolder $file.Name } else { $file.FullName }
        Copy-Item -LiteralPath $payloadPath -Destination $destination -Force
    }
}
finally {
    if ($wasRunning -eq 'true') { Invoke-Docker -Arguments @('start', $OrleansContainer) }
}
Write-Output "Installed. File backup: $backup"
Write-Output 'Review Orleans logs and reconnect clients. See docs/01-Operations.md for the smoke test.'

