param(
    [Parameter(Mandatory = $true)][string]$ServerAssemblies
)
$ErrorActionPreference = 'Stop'
$sdk = (& dotnet --list-sdks | Select-Object -Last 1) -replace '^([\d.]+) \[(.+)\]$', '$2\$1'
$compiler = Join-Path $sdk 'Roslyn\bincore\csc.dll'
$names = @('NQutils.dll', 'Interfaces.dll', 'Newtonsoft.Json.dll', 'Orleans.Core.dll',
    'Orleans.Core.Abstractions.dll', 'Orleans.Runtime.Abstractions.dll', 'Backend.dll', 'Backend.PubSub.dll',
    'netstandard.dll', 'Microsoft.Win32.Primitives.dll', 'YamlDotNet.dll')
$references = @($names | ForEach-Object { Join-Path $ServerAssemblies $_ })
$references += @(Get-ChildItem -LiteralPath $ServerAssemblies -Filter 'System*.dll' | ForEach-Object FullName)
$output = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$options = @('/nostdlib+', '/target:library', '/langversion:10', '/optimize+', '/warnaserror+',
    ('/out:"{0}"' -f (Join-Path $output 'ModCustomElements.dll')))
$options += $references | ForEach-Object { '/reference:"{0}"' -f $_ }
$options += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { '"{0}"' -f $_.FullName }
$response = Join-Path $PSScriptRoot 'compile-mod.rsp'
Set-Content -LiteralPath $response -Value $options -Encoding utf8
& dotnet $compiler ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Custom Elements compilation failed.' }
$loaderOptions = $options | Where-Object { $_ -notmatch '^/out:' -and $_ -notmatch '\.cs"$' }
$loaderOptions += '/out:"' + (Join-Path $output 'ModCustomElementsLoader.dll') + '"'
$loaderOptions += '/reference:"' + (Join-Path $output 'ModCustomElements.dll') + '"'
$loaderOptions += '"' + (Join-Path $PSScriptRoot 'Loader\MyDuMod.cs') + '"'
$loaderResponse = Join-Path $PSScriptRoot 'compile-loader.rsp'
Set-Content -LiteralPath $loaderResponse -Value $loaderOptions -Encoding utf8
& dotnet $compiler ('@' + $loaderResponse)
if ($LASTEXITCODE -ne 0) { throw 'Custom Elements loader compilation failed.' }

