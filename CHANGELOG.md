# Changelog

## 1.0.0 — unreleased candidate

First container-oriented release of the private activity archive and walking adventures. Independent API Git/image, .NET 10 LTS, safe offline owner bootstrap/recovery, persistent data/protection state, health probes, bounded archive reads/exports and complete dependency notices.

Single-active passports support leaving, confirmed switching and resuming without deleting saved progress. The upgrade keeps each profile's oldest passport active and saves the others as inactive.

Pushes to `main`/`master` automatically verify, build, scan and publish API images to GHCR. Default-branch images receive `latest`, branch and full-commit tags; matching version tags create non-overwritten numbered releases. Pull requests verify without publication.

Image scanning now uses the runner's UID/GID and a writable private cache home, preventing root-owned cache files from failing cleanup. Offline wrapper regressions cover successful scans, security blocks, malformed reports and scanner/export failures.

Optional trusted-LAN HTTP hosting now uses an explicit private-IP origin and a separate HttpOnly/Strict session cookie. Narrow dedicated private proxy CIDRs support dynamic Docker addresses without disabling origin/CSRF checks. HTTPS remains the default; public HTTP clients and broad/public trusted networks are rejected.

Optional `Hosting:LanHttpOrigin` lets the HTTPS domain and private-IP HTTP coexist. Cookies are selected per transport without changing authentication/key purposes; both sessions persist across recreation. Cross-origin requests between the registered accesses remain rejected.

Updated the pinned ASP.NET runtime base to the .NET 10.0.15 November security release for CVE-2026-114146, CVE-2026-114147 and CVE-2026-114149; the SDK and Ubuntu package pins remain unchanged.

Migrations preserve activity, history, original start dates and stamps. See [release notes](RELEASE_NOTES.md) and [CasaOS upgrade/recovery](docs/CASAOS.md) for compatibility and operator checks.
