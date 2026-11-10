#!/usr/bin/env bash
# Offline wrapper regressions, not a substitute for the real Trivy image scan.
set -Eeuo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
command -v jq >/dev/null || { echo 'jq is required for scanner-wrapper tests.' >&2; exit 1; }
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
mkdir -p "$work/bin"

cat > "$work/bin/docker" <<'DOCKER'
#!/usr/bin/env bash
set -Eeuo pipefail
if [[ "${1:-}" == image && "${2:-}" == save && "${3:-}" == --output ]]; then
  [[ "$SCAN_TEST_CASE" != export-error ]] || exit 17
  : > "$4"
  exit 0
fi
[[ "${1:-}" == run ]] || exit 90
shift
user=''; home=''; output=''
while (( $# )); do
  case "$1" in
    --user) user="$2"; shift 2 ;;
    --env)
      [[ "$2" != HOME=* ]] || home="$2"
      shift 2 ;;
    --mount)
      if [[ "$2" == type=bind,source=*,target=/output ]]; then
        output="${2#type=bind,source=}"; output="${output%,target=/output}"
      fi
      shift 2 ;;
    --tmpfs) shift 2 ;;
    *) shift ;;
  esac
done
if [[ "$user" != "$(id -u):$(id -g)" || "$home" != HOME=/cache || -z "$output" ]]; then
  echo 'The scanner must use the caller UID/GID and its writable cache as HOME.' >&2
  exit 91
fi
[[ "$SCAN_TEST_CASE" != scanner-error ]] || exit 42
mkdir -p "$output/cache/db" "$output/cache/fanal"
printf 'fixture' > "$output/cache/db/trivy.db"
chmod 700 "$output/cache/db" "$output/cache/fanal"
case "$SCAN_TEST_CASE" in
  high|critical)
    jq -n --arg severity "${SCAN_TEST_CASE^^}" '{Results:[{Vulnerabilities:[{Severity:$severity}]}]}' > "$output/raw.json" ;;
  lower-severity)
    printf '%s\n' '{"Results":[{"Vulnerabilities":[{"Severity":"LOW"},{"Severity":"MEDIUM"}]}]}' > "$output/raw.json" ;;
  secret)
    printf '%s\n' '{"Results":[{"Secrets":[{"RuleID":"synthetic-rule","Category":"fixture","Severity":"LOW","Title":"Synthetic secret","StartLine":1,"EndLine":1,"Match":"synthetic-private-value","Code":{"Lines":["synthetic-private-value"]}}]}]}' > "$output/raw.json" ;;
  invalid-report) printf '%s\n' 'not-json' > "$output/raw.json" ;;
  *) printf '%s\n' '{"Results":[]}' > "$output/raw.json" ;;
esac
DOCKER
# A Bash double also works when temporary directories are mounted noexec.
docker() { bash "$SCAN_TEST_DOCKER_STUB" "$@"; }
export -f docker

fail() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }
run_case() {
  local name="$1" expected="$2" actual directory="$work/$1"
  mkdir -p "$directory/run" "$directory/tmp"
  if (
    cd -- "$directory/run"
    SCAN_TEST_DOCKER_STUB="$work/bin/docker" TMPDIR="$directory/tmp" SCAN_TEST_CASE="$name" \
      bash "$root/scripts/scan-image.sh" synthetic:scanner-wrapper
  ) > "$directory/output.log" 2>&1; then actual=0; else actual=$?; fi
  if [[ "$expected" == failure ]]; then
    [[ "$actual" != 0 ]] || fail "$name unexpectedly succeeded"
  elif [[ "$actual" != "$expected" ]]; then
    cat "$directory/output.log" >&2
    fail "$name returned $actual, expected $expected"
  fi
  [[ -z "$(find "$directory/tmp" -mindepth 1 -print -quit)" ]] || fail "$name left its private scan directory behind"
  if [[ "$name" == secret ]]; then
    local report="$directory/run/artifacts/image-security/report.json"
    if grep -Fq 'synthetic-private-value' "$report"; then fail 'Matched secret material escaped report sanitization'; fi
    jq -e '.Results[0].Secrets[0] | (has("Match") or has("Code")) | not' "$report" >/dev/null \
      || fail 'Secret report retained raw match/code fields'
  fi
  printf 'PASS: %s\n' "$name"
}

run_case clean 0
run_case lower-severity 0
run_case high 1
run_case critical 1
run_case secret 1
run_case export-error 17
run_case scanner-error 42
run_case invalid-report failure
