#!/usr/bin/env bash
# Prints an Entra access token for Azure DevOps that can read the private Azure Artifacts feed,
# masked for GitHub Actions logs. Requires an earlier azure/login (OIDC) step in the same job.
set -euo pipefail

# Azure DevOps resource (application) ID; the same for every organization.
readonly AZURE_DEVOPS_RESOURCE="499b84ac-1321-427f-aa17-267ca6975798"

token="$(az account get-access-token --resource "${AZURE_DEVOPS_RESOURCE}" --query accessToken --output tsv)"
if [[ -z "${token}" ]]; then
  echo "get-feed-token.sh: az returned no Azure DevOps token" >&2
  exit 1
fi

if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
  # Stderr, so the mask command never ends up in a captured token.
  echo "::add-mask::${token}" >&2
fi
printf '%s' "${token}"
