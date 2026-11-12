#!/usr/bin/env bash
set -euo pipefail

resolver="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/resolve-build-hosts.sh"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
mkdir "$scratch/bin"
cat > "$scratch/bin/getent" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == ahostsv4 ]]
case "$MOCK_DNS_CASE" in
  lookup-error|fallback-success|fallback-security-error) exit 2 ;;
  empty) exit 0 ;;
  ipv6) printf '2001:db8::1 STREAM example\n'; exit 0 ;;
  bad-ip) printf '192.0.2.999 STREAM example\n'; exit 0 ;;
  security-error) [[ "$2" != security.ubuntu.com ]] || exit 2 ;;
esac
printf '192.0.2.10 STREAM example\n192.0.2.10 DGRAM example\n192.0.2.11 STREAM example\n'
MOCK
chmod 700 "$scratch/bin/getent"
cat > "$scratch/bin/node" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$1" == */resolve-build-hosts-https.mjs ]]
case "$MOCK_DNS_CASE" in
  fallback-success) ;;
  fallback-security-error) [[ "$2" != security.ubuntu.com ]] || exit 1 ;;
  *) exit 1 ;;
esac
printf '192.0.2.10\n192.0.2.11\n'
MOCK
chmod 700 "$scratch/bin/node"

expected=$'archive.ubuntu.com:192.0.2.10\narchive.ubuntu.com:192.0.2.11\nsecurity.ubuntu.com:192.0.2.10\nsecurity.ubuntu.com:192.0.2.11'
actual="$(MOCK_DNS_CASE=success PATH="$scratch/bin:$PATH" bash "$resolver")"
[[ "$actual" == "$expected" ]]
printf 'PASS: IPv4 mappings, both mirrors and duplicate removal\n'
actual="$(MOCK_DNS_CASE=fallback-success PATH="$scratch/bin:$PATH" bash "$resolver" 2> "$scratch/error")"
[[ "$actual" == "$expected" ]]
printf 'PASS: system DNS failure uses HTTPS fallback for both mirrors\n'

for scenario in lookup-error empty ipv6 bad-ip security-error fallback-security-error; do
  if actual="$(MOCK_DNS_CASE="$scenario" PATH="$scratch/bin:$PATH" bash "$resolver" 2> "$scratch/error")"; then
    printf 'FAIL: expected rejection for %s\n' "$scenario" >&2
    exit 1
  fi
  [[ -z "$actual" ]]
  grep -Fq IPv4 "$scratch/error"
  printf 'PASS: %s rejected without partial output\n' "$scenario"
done
