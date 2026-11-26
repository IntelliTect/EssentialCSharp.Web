#!/usr/bin/env bash
# Prints a token that can read the private Azure Artifacts feed, masked for GitHub Actions logs.
#
# FEED_AUTH_MODE=oidc: an Entra access token for Azure DevOps from the current `az login`
#   (azure/login with OIDC must have run in an earlier step of the same job).
# FEED_AUTH_MODE=pat (default): the legacy personal access token in AZURE_DEVOPS_PAT.
set -euo pipefail

# Azure DevOps resource (application) ID; the same for every organization.
readonly AZURE_DEVOPS_RESOURCE="499b84ac-1321-427f-aa17-267ca6975798"

mode="${FEED_AUTH_MODE:-pat}"
case "${mode}" in
  oidc)
    token="$(az account get-access-token --resource "${AZURE_DEVOPS_RESOURCE}" --query accessToken --output tsv)"
    ;;
  pat)
    token="${AZURE_DEVOPS_PAT:-}"
    ;;
  *)
    echo "get-feed-token.sh: unknown FEED_AUTH_MODE '${mode}' (expected oidc or pat)" >&2
    exit 1
    ;;
esac

if [[ -z "${token}" ]]; then
  echo "get-feed-token.sh: no feed token available for FEED_AUTH_MODE=${mode}" >&2
  exit 1
fi

if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
  # Stderr, so the mask command never ends up in a captured token.
  echo "::add-mask::${token}" >&2
fi
printf '%s' "${token}"
