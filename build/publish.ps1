# Builds the portable GUI release: artifacts/Drydock-<version>-win-x64.zip
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$name = "Drydock-$Version-win-x64"
$out = Join-Path $root "artifacts\$name"
$zip = "$out.zip"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
if (Test-Path $zip) { Remove-Item -Force $zip }

dotnet publish (Join-Path $root 'src\Drydock.App') -c Release -r win-x64 --self-contained -p:Version=$Version -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# the game's own assemblies are loaded from the install at runtime and must never ship
$game = Get-ChildItem -Recurse $out -Include 'VRage*', 'SpaceEngineers*', 'Keen*'
if ($game) { throw "Game binaries in the release: $($game.Name -join ', ')" }

Compress-Archive -Path "$out\*" -DestinationPath $zip
Write-Host "Built $zip"
