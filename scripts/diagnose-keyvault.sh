#!/usr/bin/env bash
set -euo pipefail
# Diagnose Key Vault reference issues for the Function App
# Usage:
#  ./scripts/diagnose-keyvault.sh --rg rg-graph-cal-dev --func grcal-dev-func [--kv <vaultName>]
#
# What it does:
# 1. Resolve Key Vault name (if not provided)
# 2. Show Function App identity principalId
# 3. List role assignments on the vault for that principal
# 4. List secrets in the vault
# 5. Show the exact App Settings that contain KeyVault references
# 6. Provide recommended remediation steps

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rg) RG="$2"; shift;;
    --func) FUNC="$2"; shift;;
    --kv) KV_NAME="$2"; shift;;
    --help|-h)
      grep '^#' "$0" | sed 's/^# //'; exit 0;;
    *) echo "Unknown arg: $1" >&2; exit 1;;
  esac
  shift
done

: "${RG:?--rg required}" 
: "${FUNC:?--func required}" 

if [[ -z "${KV_NAME:-}" ]]; then
  echo "[info] Resolving Key Vault in resource group..." >&2
  KV_NAME=$(az keyvault list -g "$RG" --query "[0].name" -o tsv || true)
fi

if [[ -z "${KV_NAME:-}" ]]; then
  echo "[error] Could not find any Key Vault in RG $RG" >&2
  exit 1
fi

echo "[info] Using Key Vault: $KV_NAME" >&2
VAULT_ID=$(az keyvault show -n "$KV_NAME" -g "$RG" --query id -o tsv)

PRINCIPAL_ID=$(az resource show -g "$RG" -n "$FUNC" --resource-type Microsoft.Web/sites --query identity.principalId -o tsv)
if [[ -z "$PRINCIPAL_ID" ]]; then
  echo "[error] Function has no system-assigned identity enabled" >&2
  exit 1
fi

echo "Function Identity PrincipalId: $PRINCIPAL_ID"

echo "[info] Role assignments on vault for this principal:" >&2
az role assignment list --assignee "$PRINCIPAL_ID" --scope "$VAULT_ID" -o table || true

echo "[info] Listing vault secrets (names only):" >&2
az keyvault secret list --vault-name "$KV_NAME" --query '[].name' -o tsv || true

echo "[info] Fetching Function App settings (filtered for KeyVault references):" >&2
az functionapp config appsettings list -g "$RG" -n "$FUNC" \
  --query "[?starts_with(value, '@Microsoft.KeyVault')].[name,value]" -o table || true

echo "\n=== Recommended Checks ==="
cat <<EOF
1. Ensure role 'Key Vault Secrets User' is present above. If missing:
   az role assignment create --assignee $PRINCIPAL_ID --role 'Key Vault Secrets User' --scope $VAULT_ID
2. Wait up to 5-10 minutes for AAD RBAC propagation (often <2m). Then restart:
   az functionapp restart -g $RG -n $FUNC
3. Confirm no Key Vault firewall restrictions are blocking public Azure services.
4. Secret URIs in app settings must use secretUriWithVersion. Current template already does that.
5. If still failing, temporarily set plain secret values (remove @Microsoft.KeyVault...) to isolate whether it's RBAC or syntax.
6. After success, revert to Key Vault references to keep secrets centralized.
EOF
