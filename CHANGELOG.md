# Changelog

## 1.0.0 — unreleased candidate

First container-oriented release of the private activity archive and walking adventures. Independent API Git/image, .NET 10 LTS, safe offline owner bootstrap/recovery, persistent data/protection state, health probes, bounded archive reads/exports and complete dependency notices.

Single-active passports support leaving, confirmed switching and resuming without deleting saved progress. The upgrade keeps each profile's oldest passport active and saves the others as inactive.

Pushes to `main`/`master` automatically verify, build, scan and publish API images to GHCR. Default-branch images receive `latest`, branch and full-commit tags; matching version tags create non-overwritten numbered releases. Pull requests verify without publication.

Image scanning now uses the runner's UID/GID and a writable private cache home, preventing root-owned cache files from failing cleanup. Offline wrapper regressions cover successful scans, security blocks, malformed reports and scanner/export failures.

Optional trusted-LAN HTTP hosting now uses an explicit private-IP origin and a separate HttpOnly/Strict session cookie. Narrow dedicated private proxy CIDRs support dynamic Docker addresses without disabling origin/CSRF checks. HTTPS remains the default; public HTTP clients and broad/public trusted networks are rejected.

Migrations preserve activity, history, original start dates and stamps. See [release notes](RELEASE_NOTES.md) and [CasaOS upgrade/recovery](docs/CASAOS.md) for compatibility and operator checks.
