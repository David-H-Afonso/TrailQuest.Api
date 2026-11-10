# Changelog

## 1.0.0 — unreleased candidate

First container-oriented release of the private activity archive and walking adventures. Independent API Git/image, .NET 10 LTS, safe offline owner bootstrap/recovery, persistent data/protection state, health probes, bounded archive reads/exports and complete dependency notices.

Single-active passports support leaving, confirmed switching and resuming without deleting saved progress. The upgrade keeps each profile's oldest passport active and saves the others as inactive.

Pushes to `main`/`master` automatically verify, build, scan and publish API images to GHCR. Default-branch images receive `latest`, branch and full-commit tags; matching version tags create non-overwritten numbered releases. Pull requests verify without publication.

Migrations preserve activity, history, original start dates and stamps. See [release notes](RELEASE_NOTES.md) and [CasaOS upgrade/recovery](docs/CASAOS.md) for compatibility and operator checks.
