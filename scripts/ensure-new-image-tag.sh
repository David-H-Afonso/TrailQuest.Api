#!/usr/bin/env bash
set -Eeuo pipefail
package="${1:?package name required}"
version="${2:?version required}"
: "${GITHUB_REPOSITORY_OWNER:?GitHub owner required}"
: "${GH_TOKEN:?Scoped workflow token required}"
[[ "$package" =~ ^[a-z0-9-]+$ && "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
owner_type="$(gh api "users/$GITHUB_REPOSITORY_OWNER" --jq .type)"
case "$owner_type" in Organization) scope=orgs ;; User) scope=users ;; *) exit 1 ;; esac
result="$(mktemp)"
trap 'rm -f -- "$result"' EXIT
page=1
while true; do
  status="$(curl --silent --show-error --output "$result" --write-out '%{http_code}' \
    --header "Authorization: Bearer $GH_TOKEN" --header 'Accept: application/vnd.github+json' \
    "https://api.github.com/$scope/$GITHUB_REPOSITORY_OWNER/packages/container/$package/versions?per_page=100&page=$page")"
  if [[ "$status" == 404 && "$page" == 1 ]]; then exit 0; fi
  if [[ "$status" != 200 ]]; then printf 'Could not verify image immutability (HTTP %s); refusing publication.\n' "$status" >&2; exit 1; fi
  if jq -e --arg version "$version" 'any(.[]; .metadata.container.tags | index($version))' "$result" >/dev/null; then
    printf 'This image version already exists; choose a new version.\n' >&2
    exit 1
  fi
  [[ "$(jq length "$result")" != 0 ]] || exit 0
  page=$((page+1))
done
