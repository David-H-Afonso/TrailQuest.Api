# TrailQuest API 1.0.0

Private activity archive and walking adventures, packaged as independent API and web services.

- Independent API Git repository/image, .NET 10 LTS and locked NuGet dependencies.
- Automatic GHCR builds on `main`/`master` pushes, with reusable verification, pre-publication image scanning, `latest`/commit tags and optional immutable numbered releases. Pull requests never publish; CasaOS deployment remains manual.
- No embedded accounts, passwords, Garmin credentials or server-specific addresses. Owner creation/recovery uses a hidden private console; remote first-owner setup remains disabled.
- Persistent `/data` and `/security`, non-root/read-only container profile, health/readiness probes and single-instance storage protection.
- Explicit private-LAN HTTP opt-in with private-IP/client validation, a separate HttpOnly/Strict cookie and dedicated private proxy CIDRs for dynamic Docker addresses. HTTPS remains the default and is required for Internet access; LAN mode requires a new compatible API image.
- Simultaneous HTTPS-domain and LAN HTTP through optional `Hosting:LanHttpOrigin`, transport-specific cookies and persistent Compose settings. No schema or key-purpose change is required.
- Walking adventures, manual Garmin archives, FIT/GPX/TCX/ZIP activities, recorded strength, private sharing and revocable application read grants retained.
- Optional examples disabled by default; personal profiles, history, start dates and earned stamps are preserved.
- One active passport per profile, explicit leave/switch/resume and preserved inactive progress. Upgrades keep the oldest enrollment active and archive the others without deleting history or stamps.
- Activity track counts are backfilled additively. Summary/strength reads no longer load every GPS trace; exports stream, support dates and have explicit concurrency/work limits.
- MIT for original code; complete dependency notices and application CycloneDX inventory retained. Garmin FIT remains under its separate agreement, not MIT or automatic redistribution clearance.

**Upgrade:** stop the old API, make a protected matching data/security backup, pin the paired image versions, then apply forward migrations once. Do not share one SQLite volume across running versions. Roll back with matching images and backup, not destructive migration-down commands.

**Not included:** central SSO, email password recovery, native mobile tracking, medical interpretation, JSON restore or public deployment automation. Live Garmin/device and actual proxy/DNS/TLS verification are operator checks.
