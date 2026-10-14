#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/nuget/feed.sh
source "${script_dir}/feed.sh"

token="${1:-${NUGET_FEED_TOKEN:-}}"
username="${2:-${FEED_USERNAME}}"

if [[ -z "${token}" ]]; then
  echo "emit-creds-config.sh: no token (pass as \$1 or set NUGET_FEED_TOKEN)" >&2
  exit 1
fi

xml_escape() {
  local value="${1}"
  value="${value//&/&amp;}"
  value="${value//</&lt;}"
  value="${value//>/&gt;}"
  value="${value//\"/&quot;}"
  printf '%s' "${value}"
}

if [[ ! "${FEED_NAME}" =~ ^[A-Za-z_][A-Za-z0-9._-]*$ ]]; then
  echo "emit-creds-config.sh: FEED_NAME must be a valid NuGet.Config XML element name" >&2
  exit 1
fi

escaped_username="$(xml_escape "${username}")"
escaped_token="$(xml_escape "${token}")"

cat <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSourceCredentials>
    <${FEED_NAME}>
      <add key="Username" value="${escaped_username}" />
      <add key="ClearTextPassword" value="${escaped_token}" />
    </${FEED_NAME}>
  </packageSourceCredentials>
</configuration>
EOF
