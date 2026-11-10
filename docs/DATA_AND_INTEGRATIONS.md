# Data, synchronization and integrations

## Scope and implementation status

TrailQuest targets a small private/family self-hosted installation, with one Garmin connection per user and installation. The API and frontend are independent repositories/images. Stored records belong to the authenticated user; browsing them does not contact Garmin.

Implemented: content-driven passports; daily steps; optional demo fixtures; broad manual Garmin import; opt-in in-process 12h/24h import; stored successful data responses and normalized metrics; recorded activities/strength; FIT/GPX/TCX/ZIP parsing; friend-only publications; revocable per-user application read grants; disabled-by-default remote hosting configuration. **A central OpenID Connect provider, cross-app account linking and single sign-on are not installed or implemented yet.** See the separate identity design below.

## Human first run with existing data

1. Stop TrailQuest. Do not copy a live SQLite main file while ignoring WAL/SHM.
2. Before starting the updated app against personal data, run `pwsh -File scripts/backup.ps1` on Windows. The explicit command writes a coherent SQLite snapshot to a new `Downloads/trailquest-backup-<timestamp>/trailquest.db`, checks integrity and exits before migrations or protection-store access. It refuses to overwrite an existing backup.
3. Equivalent Linux CLI, from the API repository: `dotnet run --project TrailQuest.Api.csproj --no-launch-profile -- --Backup:Destination="$HOME/Downloads/trailquest-backup-YYYYMMDD/trailquest.db"`. Select `Data__Directory` explicitly for existing installations, use a new destination and keep it private.
4. Start normally. The generated `ExpandAdventureAndHealthArchive` migration only adds tables and journey content/version columns. It does not reset profiles, daily totals, accounts or credentials. Existing journeys get their baseline content snapshot with their IDs/start dates/rewards retained.
5. To roll back, first agree which subsequent activity must be preserved, stop the app, make another coherent backup, and restore only the matching snapshot and source version. Do not run migration `Down` on valuable new data merely to “undo” the feature: it drops the new archive/social/grant tables.

The 1.0 `AddActivityTrackPointCount` migration adds/backfills cached point counts, retaining original JSON/binaries/history. Container recovery also requires the matching security state; see [CasaOS recovery](CASAOS.md). Never start an old/new API concurrently over the same data directory.

## Passport enrollment

Only one passport is active in each isolated profile. `POST /api/v1/profiles/{kind}/journeys` starts or resumes one; replacing an active passport requires its ID in `replacingRouteId`, so a stale tab cannot silently replace a newly selected route. `DELETE /api/v1/profiles/{kind}/journeys/{routeId}` leaves the owned passport without deleting its record, first start date or earned stamps.

Leaving freezes that passport's recorded progress. Resuming preserves its banked progress and starts a new counting period today, subtracting today's already-stored total rather than counting inactive days. Explicitly changing the active passport's counting date recalculates from that date while preserving earned rewards. With no active passport, steps, health, activities and saved passports remain available; the home screen offers route selection.

The single-active upgrade preserves all enrollments and keeps the oldest `startedOn`, then `createdAtUtc`, then ID active in each profile. Other rows become inactive and receive a one-time progress snapshot using the profile's calendar. A filtered unique database index enforces the rule thereafter. Personal/demo and other users never replace one another's passports.

The source-only pre-edit checkpoint is separate from the private database. Credentials still require the existing OS-local protection store; this backup command intentionally does not copy protection keys or print credentials. Losing that store requires reconnecting Garmin. Never expose a database, key directory or backup as static web content.

## Disposable demo data

`Content/Demo/sample.json` is the only production demo fixture source: relative dates, fictional activities, Garmin-shaped health records, optional sample journeys and fictional feed cards. It is **disabled by default**, including the container. No fake accounts or friendships are created. The frontend consumes ordinary authenticated APIs in both modes, defaults to personal and waits for availability before choosing a profile.

Remove the fixture, set `Demo__Enabled=false`, or point `Demo__FixturePath` at an absent file; then **reset only the demo profile** from Settings' secondary demo-data section to remove already-seeded examples. Personal data is never reset. Without fixtures, the selector is hidden and legitimate empty states remain usable. API tests own `tests/TrailQuest.Api.Tests/Fixtures`; frontend tests own synthetic contract fixtures and do not import the API repository. Adventure/challenge content is not mock data and must not be deleted with disposable examples.

## Import behavior

The current official inventory has 26 read-only data endpoints, represented by 22 selectable groups plus activity detail/workout/original handling. Datasets include daily summaries, sleep, pulse, HRV, Body Battery, stress, hydration, SpO2, weight, nutrition, fitness age and optional sensitive profile/reproductive-health records. Availability varies by device/account. These are stored records, not live sensor streams.

Save the selected groups in **Settings → Tu archivo Garmin**. Sensitive groups are not selected implicitly. Both sync buttons use the same broad import service and the saved selection. No screen GET triggers a Garmin call.

- One user operation can make many bounded upstream requests. Requests cover at most 28 inclusive days, including datasets whose upstream maximum is larger. Import older history in explicit successive ranges.
- Activity search is paginated, at most 500 records per batch. Originals are limited to 10 attempts per batch; pending originals need another import of the **same period**. No unbounded historical crawl runs automatically.
- Daily totals replace overlapping totals and are idempotent. Daily distance is not combined with overlapping GPS activity distance.
- Health records retain original successful data fields, including unmodeled/envelope fields; latest documents are upserted by source identity. This is not an immutable archive of every revision ever returned. Unchanged responses do not create duplicate history.
- Records are normalized for current cards/charts/challenges. Unknown fields remain inspectable privately. Missing values are not fabricated zeroes; performed sets are not inferred from workout instructions.
- Activity detail/workout payloads are currently fetched for newly discovered activities; list summaries are refreshed. Re-decoding a changed historical FIT/original or deep reconciliation of every previously imported detail is not yet a full historical versioning system.
- Partial failures are reported per dataset. Imported results remain; unreturned days are not erased. The client refreshes a bearer once after a 401 and never follows upstream redirects. A credential-version change rejects stale import writes.
- Successful JSON data is limited to 8 MiB per response; originals to 32 MiB. ZIP parsing is in memory with entry/expansion limits and no filesystem extraction. XML disables DTD/external entities. Invalid FIT CRC/integrity is rejected.
- A fingerprint of the decoded original prevents duplicate upload/import of the same original, including a Garmin-imported file. No perfect deduplication is promised for unrelated export files with different content or incomplete multisport records.
- Raw health responses and originals are private database data, not technical logs. They are **not encrypted by SQLite itself**. Garmin credentials are separately protected; protect the database volume, OS account, disk and backups appropriately.

The UI initially shows 28 days and offers 7/28/90/365-day or custom stored-history periods, at most 366 inclusive days. The API uses `startDate`/`endDate`. These reads never fetch Garmin. Detailed statistical reports, medical interpretation and scheduled report delivery are not implemented.

## Bounded reads and export

Health overview/raw-document and export selections are checked transactionally before loading JSON: at most 10,000 selected records and 8,388,608 stored JSON characters. Summaries do not read full GPS/original binaries; strength totals aggregate in SQLite. An oversized selection returns `archive_too_large`/413 with no silent truncation. Raw documents support `limit=1..31` for a smaller selection.

`GET /api/v1/profiles/{kind}/export` streams the version-2 JSON rather than creating a second complete byte buffer. Optional **paired** `startDate`/`endDate` select up to 366 days of daily records, activities and health documents. Journey definitions/current progress and achievements remain complete, not recalculated against a partial export. The response records the range and `X-TrailQuest-Export-Range`; the frontend refuses to mislabel an older API's unfiltered response. Original recordings and credentials are excluded. Export is limited to four requests/minute and one in progress per account. Use explicit date partitions for large archives; JSON restore is not implemented.

## Optional periodic synchronization

Default: manual (`IntervalHours=0`). Only an authenticated user selecting 12h or 24h and saving enables the schedule for that account. The backend checks due opted-in accounts while it is running; nothing is installed in Task Scheduler, cron, systemd or CasaOS. Closed app / powered-off host means no execution. A due account can catch up while the app next runs; this does not force the watch to upload to Garmin Connect.

One import per user can run at a time. Last-attempt time limits repeated scheduled attempts. Disconnected installations are skipped. Only the installation with that user's decryptable local connection can run its import. A lifetime storage lock prevents simultaneous 1.0 API/maintenance processes in one data directory; it is not distributed job leasing and older versions must still be stopped explicitly.

## Actual GPS and strength

Original activity files are downloaded through the official API, then parsed using the owner-approved `Garmin.FIT.Sdk` or the bounded XML reader. The SDK has its own FIT license, **not MIT**; review distribution terms before shipping binaries publicly. Original files remain downloadable privately. The UI defaults to an offline geographic trace and point/time table. An explicit street-map button loads OpenStreetMap tiles and explains that the provider can infer the viewed area; no browser geolocation or reverse geocoding is used. A provider failure returns to the private schematic, with human-triggered retry only.

Indoor activities may have no GPS. GPS accuracy remains that of the recording. FIT active sets can provide performed reps/load; rest sets and planned workouts do not count as performed work. Summary-set volume units are converted from grams×reps to kg×reps; unsupported/bodyweight loads stay unknown. Challenges use recorded volume/repetitions/session counts and explain narrative comparisons rather than prescribing extreme exercise.

## Sharing and friends

Only accepted, unblocked current friendships can read each other's published snapshots. Friendship is rechecked on reads; removing/blocking ends access to the other account's posts. Own posts remain readable/removable. The feed is chronological, bounded, and has no public discovery or engagement ranking.

Share previews now accept the authenticated actor's `profileId` (`demo` or `personal`, default personal). A demo stamp/activity can be previewed in place without navigating to a form that is hidden in demo mode. Demo publication is rejected server-side and never creates real friend-feed rows. Personal publication retains ownership/achievement/friendship validation.

Publishing is explicit and previewable. Earned checkpoints, completed virtual passports, reached strength challenges and owned activities are server-validated. An activity share exposes the selected summary, not sleep/pulse/raw health history. Maps are off by default. If explicitly included, points within 500m of either endpoint and all point timestamps are removed **server-side before storage/sharing**. Endpoint masking is not anonymity; the route can still reveal sensitive places. There are no secret public share links. Publishing makes the snapshot available to current accepted friends, including future accepted friendships while the post remains published.

## Provider API / application grants

Create an application grant in Settings with a name, 1–90 day expiry and explicit scopes. The credential is shown once, kept only in the component's temporary memory, and stored server-side only as SHA-256. Do not put it in a frontend bundle, browser storage, URL or shared cookie. Revoke it from the owning account.

| Endpoint | Scope | Meaning |
| --- | --- | --- |
| `GET /api/v1/provider/steps?startDate=…&endDate=…` | `steps:read` | Stored daily steps/meters/estimation/source/update time and IANA zone |
| `GET /api/v1/provider/activities?startDate=…&endDate=…` | `activities:read` | Stored activity/strength summaries, not GPS geometry or raw provider payloads |
| `GET /api/v1/provider/wellness?startDate=…&endDate=…` | `wellness:read` | Normalized daily metrics, available samples, coverage/freshness |
| `GET /api/v1/provider/activities/{id}/track` | `routes:read` | Explicitly authorized full private GPS track |

Use `Authorization: Bearer <application-grant>` from the consuming backend. Responses are versioned (`schemaVersion: 1`); the grant's user is the subject. Supplying another `userId` does not switch accounts. Browser login cookies cannot authenticate provider routes, and application grants cannot authenticate browser/account APIs. No upstream Garmin key/token is returned. Provider reads never sync Garmin. This is narrow delegated access, not an OAuth/OIDC server implementation.

## Identity plan — separate, not a fake SSO implementation

Use an optional standard OpenID Connect provider later; keep local user/data IDs in each app. Link verified external `(issuer,subject)` identities to existing accounts explicitly. Names/emails are not stable matching keys. The chosen provider must issue a consistent cross-app identity or define controlled mappings when pairwise subject IDs differ.

New SSO sign-ins necessarily depend on that provider. Existing valid app sessions and a deliberately configured local recovery login can avoid a hard dependency for every request. Do not share databases, passwords, JWT signing secrets or a parent-domain session cookie. SSO proves identity; per-user grants/scopes still authorize data access. Use authorization-code flow with PKCE and validate issuer/audience/state/nonce using maintained middleware when implemented; never use an ID token as an API access token.

## Hosting contract — operator deployment

Native development remains local-only. Production templates explicitly enable HTTPS-proxied container hosting; local verification uses disposable synthetic containers, not the actual CasaOS installation. See [the full guide](CASAOS.md).

Code supports an explicitly configured mode for a later human-managed HTTPS deployment:

- `Hosting__AllowRemote=true`
- `Hosting__PublicOrigin=https://<your-trailquest-host>` — exactly one HTTPS origin, no path/wildcards.
- `Hosting__KnownProxies__0=<exact proxy IP>` (and additional numbered IPs if required). Forwarded headers are only enabled when proxies are explicitly configured, with one-hop forwarding and an allowed-host check. Never trust arbitrary forwarded headers or all networks.
- Bind the API/reverse proxy yourself to the appropriate private container interface. Existing local launchers continue to bind loopback.

Create the owner through the **private offline container console**, or local-only setup before enabling remote mode; a fresh installation still refuses remote startup without one. Recovery targets an existing owner with the API stopped and revokes sessions without promoting members. Remote mode requires the exact HTTPS host/port, Secure/HttpOnly/host-only cookies and one-hop explicit proxy trust. The web proxy validates its own immediate forwarder and replaces headers passed to the private API. Foreign origins remain rejected, with no wildcard CORS. Backend-to-backend consumers do not need browser CORS; cross-origin browser integrations require a separate design.

The candidate targets **.NET 10 LTS**, with a pinned container runtime and explicitly approved portable SDK for local verification; system SDKs were not replaced. Actual CasaOS proxy/DNS/TLS, host permissions, user-approved live Garmin checks and protected recovery remain operator acceptance checks before exposure.
