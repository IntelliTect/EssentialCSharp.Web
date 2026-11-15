#!/usr/bin/env bash
# Waits until a deployed site reports the expected commit from GET /version.
#
# `az containerapp update` returns before traffic moves to the new revision, so smoke tests that start
# immediately can hit the old build. This polls the public URL until it answers with the expected commit
# on several consecutive requests (requests can land on different replicas while traffic shifts).
#
# Usage: wait-for-deployed-commit.sh <base-url> <expected-commit> [timeout-seconds] [required-consecutive]
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <base-url> <expected-commit> [timeout-seconds] [required-consecutive]" >&2
  exit 2
fi

base_url="${1%/}"
expected_commit="$2"
timeout_seconds="${3:-600}"
required_consecutive="${4:-3}"
poll_interval_seconds=5

if ! command -v jq >/dev/null 2>&1; then
  echo "::error::jq is required to read ${base_url}/version." >&2
  exit 2
fi

curl_error_file="$(mktemp)"
trap 'rm -f "${curl_error_file}"' EXIT

deadline=$(( $(date +%s) + timeout_seconds ))
consecutive=0
last_reported=""

echo "Waiting up to ${timeout_seconds}s for ${base_url}/version to report ${expected_commit} (${required_consecutive} consecutive times)..."

while (( $(date +%s) < deadline )); do
  # --show-error keeps curl's reason (DNS, TLS, HTTP status) for the log; a non-JSON body (e.g. a redirect
  # from a build that predates /version) leaves commit empty.
  curl_error=""
  if ! body="$(curl --silent --show-error --fail --max-time 10 --header 'Cache-Control: no-cache' "${base_url}/version" 2>"${curl_error_file}")"; then
    body=""
    curl_error="$(tr -d '\r' <"${curl_error_file}" | tail -n 1)"
  fi
  commit="$(jq -r '.commit // empty' <<<"${body}" 2>/dev/null || true)"

  if [[ "${commit}" == "${expected_commit}" ]]; then
    consecutive=$(( consecutive + 1 ))
    if (( consecutive >= required_consecutive )); then
      echo "${base_url} is serving ${expected_commit}."
      exit 0
    fi
  else
    consecutive=0
    observed="${commit:-${curl_error:-<no commit in /version response>}}"
    if [[ "${observed}" != "${last_reported}" ]]; then
      echo "Currently serving: ${observed}; still waiting..."
      last_reported="${observed}"
    fi
  fi

  sleep "${poll_interval_seconds}"
done

echo "::error::Timed out after ${timeout_seconds}s waiting for ${base_url} to serve commit ${expected_commit} (last seen: '${last_reported:-<none>}')."
exit 1
