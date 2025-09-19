#!/usr/bin/env bash
# helper: deploy infra/main.bicep with a parameters file
set -euo pipefail

# Simple helper for group deployment of main.bicep.
if [ $# -lt 2 ]; then
  echo "Usage: $0 <resource-group> <location> [parameters-file]" >&2
  exit 1
fi

RG="$1"; shift
LOC="$1"; shift
PARAM_FILE="${1:-infra/parameters.dev.json}"

echo "[+] Ensure resource group" >&2
az group create -n "$RG" -l "$LOC" 1>/dev/null

echo "[+] Validate template" >&2
az deployment group validate -g "$RG" -f infra/main.bicep -p @"$PARAM_FILE"

echo "[+] Deploy" >&2
az deployment group create -g "$RG" -f infra/main.bicep -p @"$PARAM_FILE"

echo "[+] Done" >&2