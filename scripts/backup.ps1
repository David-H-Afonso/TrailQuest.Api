#requires -Version 7.0
param([string]$DestinationDirectory, [string]$DataDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(!$DestinationDirectory){$DestinationDirectory=Join-Path $HOME ('Downloads/trailquest-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
if(!$DataDirectory){$DataDirectory=if($env:Data__Directory){[IO.Path]::GetFullPath($env:Data__Directory,$root)}else{Join-Path $root 'data'}}
if(!(Test-Path -LiteralPath (Join-Path $DataDirectory 'trailquest.db'))){throw 'No existing TrailQuest database found; no data was created or migrated.'}
$destination=Join-Path $DestinationDirectory 'trailquest.db'
if(Test-Path -LiteralPath $destination){throw 'Destination already exists; choose a fresh backup directory.'}
Write-Host 'Explicit private DB-only backup. No credentials are displayed and no protection keys are copied.'
Push-Location $root
try {
    & dotnet run --project TrailQuest.Api.csproj --no-launch-profile -- "--Data:Directory=$DataDirectory" "--Backup:Destination=$destination"
    if($LASTEXITCODE -ne 0){throw 'Backup verification failed. Do not migrate or restore until resolved.'}
} finally {Pop-Location}
Write-Host "Private backup: $destination"
