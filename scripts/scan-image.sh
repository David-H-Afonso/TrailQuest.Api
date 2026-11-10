#!/usr/bin/env bash
set -Eeuo pipefail
image="${1:?local candidate image required}"
output="$(pwd)/artifacts/image-security"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
umask 077
mkdir -p "$output" "$work/cache/tmp"
docker image save --output "$work/image.tar" "$image"
# Keep private cache/report files owned by the runner so EXIT cleanup can remove them.
docker run --rm --read-only --user "$(id -u):$(id -g)" \
  --env HOME=/cache --env TMPDIR=/cache/tmp \
  --tmpfs /tmp:rw,noexec,nosuid,size=67108864 \
  --mount "type=bind,source=$work/image.tar,target=/input/image.tar,readonly" \
  --mount "type=bind,source=$work,target=/output" \
  --mount "type=bind,source=$work/cache,target=/cache" \
  aquasec/trivy:0.76.2@sha256:8f6bb947dbc528902e11f2f5be52f7b519f6a39d5d148f41166f2040499768fb \
  image --input /input/image.tar --scanners vuln,secret --cache-dir /cache --timeout 10m --format json --output /output/raw.json
# Do not upload matched secret material as a CI artifact.
jq '.Results |= map(if .Secrets then .Secrets |= map({RuleID,Category,Severity,Title,StartLine,EndLine}) else . end)' "$work/raw.json" > "$output/report.json"
high="$(jq '[.Results[]?.Vulnerabilities[]? | select(.Severity == "HIGH" or .Severity == "CRITICAL")] | length' "$output/report.json")"
secrets="$(jq '[.Results[]?.Secrets[]?] | length' "$output/report.json")"
printf 'High/critical: %s; secret findings: %s\n' "$high" "$secrets"
[[ "$high" == 0 && "$secrets" == 0 ]]
