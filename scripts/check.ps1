#requires -Version 7.0
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & dotnet restore TrailQuest.Api.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    & dotnet build TrailQuest.Api.sln --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & dotnet test TrailQuest.Api.sln --no-build --nologo --logger 'trx;LogFileName=api.trx' --results-directory artifacts/tests
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { Pop-Location }
