# CasaOS installation, upgrade and recovery

[Español](CASAOS.es.md)

## Architecture and prerequisites

Use the API repository's **`deploy/docker-compose.yml` as the canonical project**, with separately published API/web images from the matching release. No `docker run`, temporary replacement production container or parallel stack is required. This guide does not deploy anything automatically.

The API listens privately on 8080, without a host port. Nginx Web serves the SPA and `/api` on the same origin. Your existing HTTPS reverse proxy forwards to the web service's private published port. Web trusts only its configured proxy peer; API trusts only the exact web-container IP. The dedicated Docker subnet must not overlap another network. Internet egress is needed for explicitly requested Garmin imports, not for reading saved records.

Requirements: Linux amd64, Docker Engine/Compose v2, an HTTPS origin/certificate and a reverse proxy you administer. The initial candidate was verified on an isolated local Docker engine; actual CasaOS DNS/TLS/firewall remain operator checks. ARM64 base availability is not an application verification claim; the descriptor advertises amd64 only.

## Published images

Each component repository's push to `main`/`master` verifies, builds, scans and publishes its own GHCR image. After both workflows succeed, the installation examples use `ghcr.io/david-h-afonso/trailquest-api:latest` and `ghcr.io/david-h-afonso/trailquest-web:latest`. No local compilation or application secret is needed to pull them. Forks publish under their own lower-case owner.

In GitHub **Packages → package → Package settings**, select **Public** for anonymous CasaOS pulls, or authenticate the Docker host for private packages. Public repository visibility does not by itself guarantee anonymous package access. If publication returns a permission error, check Actions' package-write permission and the package's repository/Actions access; the workflows use the built-in `GITHUB_TOKEN`, not a stored personal token.

Use the workflow summary's `sha-<full-commit>` tag or `@sha256:…` digest to pin a tested API/web pair. `latest` follows the default branch and is not immutable; numbered images appear only after pushing the matching `v<VERSION>` release tag. Keep the paired previous digests and make the agreed data/protection backup before updating. Actions never contact or deploy to your CasaOS server.

## Configure before starting

1. Copy `deploy/.env.example` to an ignored `deploy/.env`. Choose the published API/web references, preferably the tested immutable digests; do not assume `latest` is immutable.
2. Set public hostname/HTTPS port, the private bind interface/HTTP port, timezone, unused Docker subnet and service IPs. The CasaOS launcher uses the **public HTTPS address**, not HTTPS on the internal HTTP port.
3. Configure the exact immediate proxy peer seen by Web. NAT may make it different from the proxy container's own address. Never accept a CIDR/wildcard, every proxy, or client-supplied forwarding headers. Restrict the host HTTP port to the real reverse proxy with the host firewall/network policy.
4. Create two private host directories for `/data` and `/security`, owned by the configured API UID/GID. With the example's defaults, a human administrator can use:

   ```bash
   sudo install -d -m 0700 -o 1654 -g 1654 /DATA/AppData/trailquest/data /DATA/AppData/trailquest/security
   ```

   Adapt those explicit paths/IDs to your configuration. Do not chmod arbitrary directories recursively or use 0777. Compose deliberately refuses to create missing bind directories as root.

5. Validate interpolation without dumping configuration:

   ```bash
   docker compose --env-file deploy/.env -f deploy/docker-compose.yml config --quiet
   ```

No account password, Garmin credential or signing/protection key belongs in this file. `TRAILQUEST_DEMO_ENABLED` starts false. Each user's Garmin key is entered privately after login; schedules start disabled.

## First owner and launch

Keep the API stopped while provisioning. If CasaOS auto-started an empty installation, stop that service first; remote empty-account startup intentionally fails closed.

```bash
docker compose --env-file deploy/.env -f deploy/docker-compose.yml run --rm --no-deps trailquest-api --Account:Action=create-owner --Account:Username=your-chosen-name
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d
docker compose --env-file deploy/.env -f deploy/docker-compose.yml ps
```

Use a private interactive terminal; the password is hidden and confirmed. `Account:DisplayName` is optional. No default username/password is supplied. Repeating bootstrap refuses an existing owner without resetting records. Explicit stdin mode exists for managed secret input/tests; never put a real password in shell history or image/Compose environment.

Configure the external proxy to preserve Host and supply HTTPS forwarding, with upload allowance above a 32 MiB file and a timeout sufficient for a bounded Garmin batch. Do not enable wildcard CORS. Verify HTTPS login, secure host-only cookies, owner/member isolation and a real user-approved short Garmin import before sharing the installation.

For CasaOS custom-app import, use the resolved canonical Compose with your image references and private settings. Keep its two services in this one project. The metadata includes the public hostname/HTTPS port, volumes and version; it is not an app-store publication or a new domain registration.

## Upgrade

Stop the old API, make a protected matching backup, retain old immutable image references, pull the paired new images, and recreate through the same Compose project. The API applies forward migrations once. The track-count migration backfills cached GPS point counts without rewriting tracks or original recordings. Do not run two app versions against one SQLite directory.

The single-active-passport migration retains every passport, step record, start date and earned stamp. If a profile has multiple active passports, the oldest stays active, ordered by start date, creation time and ID. The others become inactive with frozen progress. Leaving/switching later follows the same preservation rule; resuming adds newly recorded steps from today rather than the inactive period. Personal/demo profiles and other users remain independent. Upgrade the paired API and web before using these controls.

Never silently copy a developer's personal database or Windows protection store into the image. Existing Windows records can be preserved through a planned transfer, but DPAPI-protected credentials require the owner to reconnect Garmin on Linux. Account/data migration must be explicitly backed up and scoped.

## Backup and restore

**DB-only snapshot:** the explicit API command `--Backup:Destination=<new-path>` uses SQLite's coherent backup API, verifies integrity, refuses overwrite and exits before migrations or key-store access. The destination must be writable and private. It intentionally does not copy protection state. JSON export is not a replacement and JSON restore is not implemented.

**Credential-preserving disaster recovery:** separately agree the scope of copying the matching private `/security` directory, including `installation.id` and the key ring. Stop the API and back up **both complete directories together** using your protected backup mechanism, preserving ownership. A plain archive contains sensitive data and cryptographic material: restrict it, preferably encrypt its storage, and never upload it to GitHub or a public share. No automatic backup schedule is installed.

To recover, preserve the current failed/new state first. Restore a verified matching backup into **new private directories**, point Compose at them, select the matching API/web image versions, then recreate the canonical project. Verify login, steps/stamps, data ownership and Garmin connection state without publishing records. Keep the old directories until the owner signs off. Do not drop tables, delete WAL files from a live database or run destructive `Down` migrations merely to roll back an image.

RPO is the last protected manual backup; no automated-backup SLA is claimed. Rehearse restore time on synthetic data and document the operator's recovery target.

## Owner recovery and troubleshooting

With API stopped, repeat the private one-off console command using `--Account:Action=recover-owner` and the **existing owner's username**. It cannot promote a member or create a recovery backdoor, and revokes that owner's sessions. Members change their own passwords; no email reset is advertised.

| Symptom | Check |
| --- | --- |
| Bind/permission error | Both actual directories exist and belong to configured UID/GID; Linux-private modes are supported |
| Existing process error | Stop the other API/maintenance process; do not delete a live lock to force access |
| 400/403 origin or HTTPS | Exact public host/port, Web's trusted peer and API's exact Web IP; Web's sanitized log includes the immediate `peer`, no cookies or body |
| Unhealthy API | `/health/ready` through the internal bounded probe, migration/startup errors, mount permissions and owner provisioning |
| 413 archive | Choose fewer days/source records or export date partitions; nothing was silently removed |
| Garmin reconnect needed | Matching security state is missing/incompatible; owner reconnects privately, never recover keys from logs/files |
| Map provider unavailable | Use the local schematic; do not automate tile retries or bulk downloads |

Retain sanitized version/request IDs, not credentials or health payloads, when reporting failures. Read [security guidance](../SECURITY.md), [third-party notices](../THIRD_PARTY_NOTICES.md) and [release notes](../RELEASE_NOTES.md) before public exposure.
