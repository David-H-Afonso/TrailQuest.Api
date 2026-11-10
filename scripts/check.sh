#!/usr/bin/env bash
set -Eeuo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
dotnet restore TrailQuest.Api.sln --locked-mode
dotnet build TrailQuest.Api.sln --no-restore --nologo
dotnet test TrailQuest.Api.sln --no-build --nologo --logger 'trx;LogFileName=api.trx' --results-directory artifacts/tests
