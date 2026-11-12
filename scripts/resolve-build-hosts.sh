#!/usr/bin/env bash
set -euo pipefail

# Resolve on the runner, not inside BuildKit. These temporary mappings are
# shared by candidate/publication builds and never become image /etc/hosts.
https_resolver="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/resolve-build-hosts-https.mjs"
entries=()
declare -A seen=()
for host in archive.ubuntu.com security.ubuntu.com; do
  if ! records="$(getent ahostsv4 "$host")" || [[ -z "$records" ]]; then
    printf 'System DNS failed for %s; trying authenticated HTTPS DNS.\n' "$host" >&2
    if ! records="$(node "$https_resolver" "$host")" || [[ -z "$records" ]]; then
      printf 'Cannot resolve Ubuntu mirror IPv4 through system or HTTPS DNS: %s\n' "$host" >&2
      exit 1
    fi
  fi
  while read -r address _; do
    if [[ ! "$address" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; then
      printf 'Invalid Ubuntu mirror IPv4 answer for %s\n' "$host" >&2
      exit 1
    fi
    IFS='.' read -r -a octets <<< "$address"
    for octet in "${octets[@]}"; do
      if (( 10#$octet > 255 )); then
        printf 'Invalid Ubuntu mirror IPv4 answer for %s\n' "$host" >&2
        exit 1
      fi
    done
    key="$host:$address"
    if [[ ! ${seen[$key]+present} ]]; then
      entries+=("$key")
      seen[$key]=1
    fi
  done <<< "$records"
done
printf '%s\n' "${entries[@]}"
