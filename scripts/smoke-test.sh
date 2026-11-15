#!/usr/bin/env bash
# Requests the health endpoint plus real pages. Usage: smoke-test.sh <fqdn>
set -euo pipefail

fqdn="${1:?usage: smoke-test.sh <fqdn>}"

# /health stays green when content-backed services are broken, so also request pages that need them.
paths=(/health / /hello-world /guidelines)

for path in "${paths[@]}"; do
  echo "GET https://${fqdn}${path}"
  # --retry-all-errors ensures HTTP 5xx (cold-start 503s) also trigger retries; -L follows page redirects
  curl --fail --silent --show-error --location --retry 10 --retry-delay 15 --retry-all-errors --max-time 30 \
    --output /dev/null --write-out '  -> %{http_code}\n' "https://${fqdn}${path}"
done
