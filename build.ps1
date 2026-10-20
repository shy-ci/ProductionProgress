param([string]$GamePath = $env:STARDEW_GAME_PATH, [string]$SmapiPath = "")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if (-not $GamePath) { throw 'Usage: .\build.ps1 -GamePath "C:\Games\Stardew Valley" [-SmapiPath "C:\SMAPI references"]' }
if (-not $SmapiPath) { $SmapiPath = $GamePath }
& dotnet build ProductionProgress.csproj -c Release "-p:GamePath=$GamePath" "-p:SmapiPath=$SmapiPath" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$manifest = Get-Content manifest.json -Raw | ConvertFrom-Json
$staging = Join-Path $PSScriptRoot 'artifacts\staging\ProductionProgress'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item $staging -ItemType Directory -Force | Out-Null
Copy-Item 'bin\Release\net6.0\ProductionProgress.dll' $staging
Copy-Item 'bin\Release\net6.0\ProductionProgress.Core.dll' $staging
Copy-Item 'manifest.json' $staging
Copy-Item 'i18n' $staging -Recurse
$archive = Join-Path $PSScriptRoot ("artifacts\ProductionProgress-" + $manifest.Version + "-preview.zip")
Compress-Archive -Path $staging -DestinationPath $archive -Force
Write-Output $archive
