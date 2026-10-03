#!/usr/bin/env bash
set -euo pipefail

: "${MIGRATION_JOB_NAME:?Set MIGRATION_JOB_NAME to the Terraform-provisioned Container Apps Job name}"
: "${MIGRATION_IMAGE:?Set MIGRATION_IMAGE to the exact image being deployed}"
: "${RESOURCEGROUP:?Set RESOURCEGROUP to the target Azure resource group}"

previous_execution_name="$(az containerapp job execution list \
  --name "$MIGRATION_JOB_NAME" \
  --resource-group "$RESOURCEGROUP" \
  --query '[0].name' \
  --output tsv)"

az containerapp job update \
  --name "$MIGRATION_JOB_NAME" \
  --resource-group "$RESOURCEGROUP" \
  --image "$MIGRATION_IMAGE"

az containerapp job start \
  --name "$MIGRATION_JOB_NAME" \
  --resource-group "$RESOURCEGROUP"

for attempt in $(seq 1 180); do
  execution_name="$(az containerapp job execution list \
    --name "$MIGRATION_JOB_NAME" \
    --resource-group "$RESOURCEGROUP" \
    --query '[0].name' \
    --output tsv)"

  if [[ -z "$execution_name" || "$execution_name" == "None" || "$execution_name" == "$previous_execution_name" ]]; then
    sleep 10
    continue
  fi

  status="$(az containerapp job execution show \
    --name "$MIGRATION_JOB_NAME" \
    --resource-group "$RESOURCEGROUP" \
    --job-execution-name "$execution_name" \
    --query 'properties.status' \
    --output tsv)"

  case "$status" in
    Succeeded)
      echo "Migration job execution $execution_name succeeded."
      exit 0
      ;;
    Failed|Stopped|Degraded)
      echo "::error::Migration job execution $execution_name finished with status '$status'." >&2
      exit 1
      ;;
    *)
      echo "Migration job execution $execution_name status: ${status:-unknown} (attempt $attempt/180)."
      sleep 10
      ;;
  esac
done

echo "::error::Timed out waiting for migration job '$MIGRATION_JOB_NAME' to complete." >&2
exit 1
