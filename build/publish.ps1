# Builds the portable release zips into artifacts/: the app and the command line tool
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Publish-Zip([string]$project, [string]$name) {
    $out = Join-Path $root "artifacts\$name-$Version-win-x64"
    $zip = "$out.zip"
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    if (Test-Path $zip) { Remove-Item -Force $zip }

    # own build folder, so a Drydock started from bin\Release doesn't lock the files
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained -p:Version=$Version `
        -p:BaseOutputPath="$root\artifacts\bin\$name\" -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed" }

    # the game's own assemblies are loaded from the install at runtime and must never ship
    $game = Get-ChildItem -Recurse $out -Include 'VRage*', 'SpaceEngineers*', 'Keen*'
    if ($game) { throw "Game binaries in $name : $($game.Name -join ', ')" }

    Compress-Archive -Path "$out\*" -DestinationPath $zip
    Write-Host "Built $zip"
}

Publish-Zip 'src\Drydock.App' 'Drydock'
Publish-Zip 'src\Drydock.Cli' 'drydock-cli'
