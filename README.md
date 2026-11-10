# TrailQuest API

[Español](README.es.md) · [Contributing](CONTRIBUTING.md) · [Security](SECURITY.md) · [License](LICENSE)

Private, self-hosted activity storage and walking adventures. Daily steps advance one active virtual passport per profile; users can keep Garmin health/activity records, import FIT/GPX/TCX files, collect stamps and explicitly share selected achievements with accepted friends. Leaving a passport preserves its progress and stamps. SQLite remains the source of truth; opening a page never starts an upstream import.

This is the **independent backend repository**. The React application and `trailquest-web` image are maintained separately. There are no bundled owner credentials or accounts, no central SSO and no public health feed. The app is not a medical or navigation service.

## Run locally

Requirements: the pinned **.NET 10 SDK** (`global.json`), or a compatible patched SDK in that feature band. Docker users do not need an SDK on the host.

```bash
dotnet restore TrailQuest.Api.sln --locked-mode
dotnet run --project TrailQuest.Api.csproj --no-launch-profile -- --urls http://127.0.0.1:5187
```

Start the separate frontend dev server and open its loopback URL. A local installation without accounts offers owner setup in the interface. New accounts start without personal records. Demo content is disabled unless explicitly enabled; removing it does not remove adventure content or affect personal progress.

By default, storage is `data/` relative to the API content root; environment configuration takes precedence. Never start multiple API versions against the same SQLite directory.

## CasaOS / Docker

Production consists of **two images**, `trailquest-api` and `trailquest-web`, one private application network and two persistent API mounts. Only the web upstream port is published. HTTPS terminates at your reverse proxy; the browser uses same-origin `/api/v1` requests.

Follow the complete [CasaOS setup and recovery guide](docs/CASAOS.md) ([Spanish](docs/CASAOS.es.md)). The image deliberately refuses remote first-owner setup. Create the owner from a private, stopped-installation console **before** starting the stack:

```bash
docker compose --env-file deploy/.env -f deploy/docker-compose.yml run --rm --no-deps trailquest-api --Account:Action=create-owner --Account:Username=your-chosen-name
docker compose --env-file deploy/.env -f deploy/docker-compose.yml up -d
```

The console asks for a hidden password twice. Do not put passwords in arguments, environment variables, Compose or image layers. Recovery of an existing owner uses `Account:Action=recover-owner`, with the API stopped; it revokes old sessions and preserves activity. Member passwords can be changed by the signed-in member; email recovery is not implemented.

## Configuration contract

Copy `deploy/.env.example` to a private, ignored `.env` next to Compose and fill **your own** installation settings. It contains examples, not real server addresses or credentials.

| Names | Purpose |
| --- | --- |
| `TRAILQUEST_API_IMAGE`, `TRAILQUEST_WEB_IMAGE` | The paired immutable image tags/digests |
| `TRAILQUEST_PUBLIC_HOST`, `TRAILQUEST_PUBLIC_HTTPS_PORT` | External HTTPS address; also used by the CasaOS launcher |
| `TRAILQUEST_BIND_ADDRESS`, `TRAILQUEST_HTTP_PORT` | Private host interface/port exposed only to the HTTPS proxy |
| `TRAILQUEST_TRUSTED_PROXY` | Exact proxy peer trusted by the web container; no CIDR/wildcard |
| `TRAILQUEST_SUBNET`, `TRAILQUEST_WEB_IP`, `TRAILQUEST_API_IP` | Unused private Docker subnet and exact service IPs |
| `TRAILQUEST_DATA_DIR`, `TRAILQUEST_SECURITY_DIR` | Persistent database and private installation/key directories |
| `TRAILQUEST_UID`, `TRAILQUEST_GID` | Runtime owner of the actual API bind-mount directories |
| `TRAILQUEST_TIME_ZONE`, `TRAILQUEST_DEMO_ENABLED` | Container timezone and explicit optional demo opt-in |

Native API names are `Data__Directory`, `Security__KeyDirectory`, `Hosting__AllowRemote`, `Hosting__PublicOrigin`, numbered `Hosting__KnownProxies__N`, `Hosting__LocalWebPort`, `Hosting__LocalApiPort`, `Demo__Enabled` and optional `Demo__FixturePath`. Garmin credentials are entered privately **inside each user's settings**, not in this configuration. Periodic Garmin import starts disabled and requires that user's explicit opt-in.

## Verify and build

```bash
bash scripts/check.sh
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project TrailQuest.Api.csproj
docker build --build-arg RELEASE_VERSION=1.0.0 -t trailquest-api:1.0.0 .
```

Windows: `pwsh -NoLogo -NoProfile -File scripts/check.ps1`. Tests create isolated synthetic databases and HTTP doubles; they never need a real Garmin key. Dockerfiles pin base-image digests, run non-root and exclude local settings, databases, keys and build outputs from context. Release builds exclude EF design-time runtime assemblies. Dependency notices and a CycloneDX application inventory are generated from published assets, not the whole development cache.

The optional GitHub workflows are **manual only**, with no scheduled scans or automatic deployment. Image publication requires a matching version tag and explicit third-party-license acknowledgement. Private repositories may consume Actions allowance; review that before dispatching. Commit, tag, repository visibility, branch protection and package visibility remain owner actions.

## Data safety and limits

- Persist **both `/data` and `/security`**. Losing the latter can invalidate sessions and require reconnecting Garmin. Windows DPAPI-protected material is not silently portable to Linux.
- Migrations preserve steps, start dates, history and earned stamps. The track-count migration derives counts without replacing GPS JSON. The single-active upgrade keeps the oldest passport active in each profile and saves the others as inactive, ordered by start date, creation time and ID. Back up before upgrading; do not use destructive down-migrations on valuable activity.
- JSON export is not a full disaster-recovery backup or an implemented JSON restore. Originals and credentials are excluded. Export supports date partitions, one concurrent export/account and explicit size/work limits; oversized selections fail without silent truncation.
- Manual Garmin batches cover at most 28 inclusive days. Stored-history reads cover up to 366 days. Files are bounded to 32 MiB, ZIP is not extracted to disk, XML entities are disabled and FIT integrity is validated.
- The archive is private, not encrypted SQLite. Protect host access, mounted directories and backups. Never mount data or protection stores as web content.

Details: [data/integrations](docs/DATA_AND_INTEGRATIONS.md), [adventure authoring](docs/AUTHORING_ADVENTURES.md), [release notes](RELEASE_NOTES.md).

## License and support

Original code: **MIT**. Photos/map geometry, third-party packages and **Garmin's FIT SDK** retain separate terms; see [third-party notices](THIRD_PARTY_NOTICES.md). The SDK's retained agreement is not a general redistribution authorization. Review its terms before publishing a binary/container. No official Garmin, map-provider or franchise affiliation is implied.

For ordinary issues, include version, deployment mode and sanitized reproduction steps. Use [SECURITY.md](SECURITY.md) for private vulnerability reporting. For startup failures, check directory ownership, exact HTTPS host/proxy trust, both volume mounts and `/health/ready`; never paste credentials or raw health exports into issues.
