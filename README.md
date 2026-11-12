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
| `TRAILQUEST_API_IMAGE`, `TRAILQUEST_WEB_IMAGE` | Published API/web image references; use release tags or digests for a pinned installation |
| `TRAILQUEST_PUBLIC_HOST`, `TRAILQUEST_PUBLIC_HTTPS_PORT` | External HTTPS address; also used by the CasaOS launcher |
| `TRAILQUEST_BIND_ADDRESS`, `TRAILQUEST_HTTP_PORT` | Private host interface/port exposed only to the HTTPS proxy |
| `TRAILQUEST_TRUSTED_PROXY` | Exact proxy peer trusted by the web container; no CIDR/wildcard |
| `TRAILQUEST_SUBNET`, `TRAILQUEST_WEB_IP`, `TRAILQUEST_API_IP` | Unused private Docker subnet and exact service IPs |
| `TRAILQUEST_DATA_DIR`, `TRAILQUEST_SECURITY_DIR` | Persistent database and private installation/key directories |
| `TRAILQUEST_UID`, `TRAILQUEST_GID` | Runtime owner of the actual API bind-mount directories |
| `TRAILQUEST_TIME_ZONE`, `TRAILQUEST_DEMO_ENABLED` | Container timezone and explicit optional demo opt-in |
| `TRAILQUEST_ALLOW_LAN_HTTP`, `TRAILQUEST_LAN_HTTP_ORIGIN` | Optional extra private-IP HTTP access alongside HTTPS; default off/empty |

Native API names are `Data__Directory`, `Security__KeyDirectory`, `Hosting__AllowRemote`, `Hosting__PublicOrigin`, numbered `Hosting__KnownProxies__N` / `Hosting__KnownNetworks__N`, `Hosting__AllowLanHttp`, optional `Hosting__LanHttpOrigin`, `Hosting__LocalWebPort`, `Hosting__LocalApiPort`, `Demo__Enabled` and optional `Demo__FixturePath`. Garmin credentials are entered privately **inside each user's settings**, not in this configuration. Periodic Garmin import starts disabled and requires that user's explicit opt-in.

### Optional HTTP on a trusted LAN

HTTPS remains the default for remote hosting. A private installation may explicitly set `Hosting__AllowLanHttp=true` with an HTTP `Hosting__PublicOrigin` containing a literal RFC1918/ULA IP, for example `http://192.168.1.50:5223`. HTTP requests from public client IPs are rejected. This mode is unencrypted: never forward its HTTP port to the Internet or use it on an untrusted network.

For dynamic Docker addresses, `Hosting__KnownNetworks__0` can trust the **dedicated app subnet**, such as `10.253.52.0/29`, rather than one fragile container IP. Only private CIDRs `/24` or narrower for IPv4 and `/64` or narrower for IPv6 are accepted; never use a shared Docker subnet or a wildcard. Keep the API port unpublished. With no external proxy yet, Web can use `TRAILQUEST_TRUSTED_FORWARDER=127.0.0.1`; its API requests remain plain HTTP.

LAN sessions retain HttpOnly, SameSite=Strict, host-only scope, origin/client-header checks and expiry. They use the separate `trailquest.lan.session` cookie without Secure; HTTPS mode retains its Secure cookie. No certificate, Caddy sidecar or static container IP is required for LAN mode. The normal Compose recipe remains HTTPS-oriented; changing environment values alone does not add this capability to older API images.

### HTTPS domain and LAN HTTP together

Keep `Hosting__PublicOrigin=https://trailquest.example`, enable `Hosting__AllowLanHttp=true` and add `Hosting__LanHttpOrigin=http://192.168.1.50:5443`. Both origins are explicit; the extra HTTP origin must be a literal private IP. HTTPS uses the host-only Secure `trailquest.session` cookie, LAN uses `trailquest.lan.session` without Secure; both remain HttpOnly/Strict. Public HTTP clients and cross-origin requests between the two accesses are rejected. Authentication/key purposes and existing LAN sessions are retained.

The HTTPS proxy forwards **HTTP** to Web, preserves Host and supplies the external scheme. Set `TRAILQUEST_TRUSTED_FORWARDER` to the exact immediate peer **observed by Web**; Docker NAT may present the app gateway rather than the proxy container IP. The canonical Compose exposes the optional LAN settings as `TRAILQUEST_ALLOW_LAN_HTTP` and `TRAILQUEST_LAN_HTTP_ORIGIN`. Apply a compatible image and complete configuration once, then retain that Compose and both persistent mounts when updating images. Commits/image pulls do not replace installed environment settings; never patch generated files inside running containers. See [CasaOS setup](docs/CASAOS.md).

## Verify and build

```bash
bash scripts/check.sh
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project TrailQuest.Api.csproj
docker build --build-arg RELEASE_VERSION=1.0.0 -t trailquest-api:1.0.0 .
```

Windows: `pwsh -NoLogo -NoProfile -File scripts/check.ps1`. Tests create isolated synthetic databases and HTTP doubles; they never need a real Garmin key. Dockerfiles pin base-image digests, run non-root and exclude local settings, databases, keys and build outputs from context. Release builds exclude EF design-time runtime assemblies. Dependency notices and a CycloneDX application inventory are generated from published assets, not the whole development cache.

## Automatic Docker images

Each push to `main` or `master` runs the reusable API checks, builds the Linux amd64 image, scans it for vulnerabilities/secrets and publishes to **GHCR** only after all checks pass. Pull requests run verification without publishing. `workflow_dispatch` remains available for supported branches and release tags; there are no scheduled runs or automatic CasaOS deployments.

| Source | Published tags |
| --- | --- |
| Default-branch push | `latest`, the branch name and `sha-<full-commit>` |
| Other supported branch | The branch name and `sha-<full-commit>`; does not replace `latest` |
| `v1.0.0` tag matching `VERSION` | `1.0.0`, `v1.0.0` and `sha-<full-commit>`; existing release versions are refused |

Ordinary commits do not need a version bump or Git tag. Once the first successful workflow has published both packages:

```bash
docker pull ghcr.io/david-h-afonso/trailquest-api:latest
docker pull ghcr.io/david-h-afonso/trailquest-web:latest
```

Publication uses the job-scoped `GITHUB_TOKEN` with `packages: write`, not a personal token or application credential. Forks publish under their own lower-case owner. The workflow summary records tags, source SHA, digest and a pinned pull command; use that digest for reproducible installs. For anonymous CasaOS pulls, make the GHCR packages public after their first publication; otherwise authenticate the Docker host. See [CasaOS setup](docs/CASAOS.md).

Review bundled dependency/photo terms, including Garmin FIT, before distribution; automation does not grant redistribution rights. Repository/package visibility, branch protection and Actions allowance remain owner settings. Private repositories may consume billable Actions minutes.

## Data safety and limits

- Persist **both `/data` and `/security`**. Losing the latter can invalidate sessions and require reconnecting Garmin. Windows DPAPI-protected material is not silently portable to Linux.
- Migrations preserve steps, start dates, history and earned stamps. The track-count migration derives counts without replacing GPS JSON. The single-active upgrade keeps the oldest passport active in each profile and saves the others as inactive, ordered by start date, creation time and ID. Back up before upgrading; do not use destructive down-migrations on valuable activity.
- JSON export is not a full disaster-recovery backup or an implemented JSON restore. Originals and credentials are excluded. Export supports date partitions, one concurrent export/account and explicit size/work limits; oversized selections fail without silent truncation.
- Settings offers a separate Garmin JSON export by inclusive date range and all/selected datasets. It can be copied or downloaded for manual review; credentials, original FIT/GPX files and GPS tracks are excluded, and sensitive datasets require confirmation. Nothing is sent to ChatGPT automatically.
- Manual Garmin batches cover at most 28 inclusive days. Stored-history reads cover up to 366 days. Files are bounded to 32 MiB, ZIP is not extracted to disk, XML entities are disabled and FIT integrity is validated.
- The archive is private, not encrypted SQLite. Protect host access, mounted directories and backups. Never mount data or protection stores as web content.

Details: [data/integrations](docs/DATA_AND_INTEGRATIONS.md), [adventure authoring](docs/AUTHORING_ADVENTURES.md), [release notes](RELEASE_NOTES.md).

## License and support

Original code: **MIT**. Photos/map geometry, third-party packages and **Garmin's FIT SDK** retain separate terms; see [third-party notices](THIRD_PARTY_NOTICES.md). The SDK's retained agreement is not a general redistribution authorization. Review its terms before publishing a binary/container. No official Garmin, map-provider or franchise affiliation is implied.

For ordinary issues, include version, deployment mode and sanitized reproduction steps. Use [SECURITY.md](SECURITY.md) for private vulnerability reporting. For startup failures, check directory ownership, exact HTTPS host/proxy trust, both volume mounts and `/health/ready`; never paste credentials or raw health exports into issues.
