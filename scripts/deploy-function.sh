#!/usr/bin/env bash
set -euo pipefail

# Build & ZIP deploy the Azure Function to an existing Function App.
# Requirements: dotnet SDK, Azure CLI logged in (az login), correct subscription selected.
# Usage:
#   ./scripts/deploy-function.sh -g <resourceGroup> -n <functionAppName> [--set-base-url]
# Options:
#   -g|--resource-group   Resource group name (required)
#   -n|--name             Function App name (required)
#   --set-base-url        Also set Webhook__BaseUrl to https://<functionApp>.azurewebsites.net
#   --no-restore          Skip explicit dotnet restore (if previously done)
#   -h|--help             Show help

RG=""
APP=""
SET_BASE_URL=0
NO_RESTORE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    -g|--resource-group) RG="$2"; shift;;
    -n|--name) APP="$2"; shift;;
    --set-base-url) SET_BASE_URL=1;;
    --no-restore) NO_RESTORE=1;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'; exit 0;;
    *) echo "Unknown arg: $1" >&2; exit 1;;
  esac
  shift
done

if [[ -z "$RG" || -z "$APP" ]]; then
  echo "-g と -n は必須です" >&2; exit 1
fi

ROOT_DIR=$(cd "$(dirname "$0")/.." && pwd)
FUNC_PROJ="$ROOT_DIR/FunctionApp/FunctionApp.csproj"
OUT_DIR="$ROOT_DIR/dist/function_publish"
ZIP_PATH="$ROOT_DIR/functionapp.zip"

echo "[info] Building (Release)" >&2
[[ $NO_RESTORE -eq 1 ]] || dotnet restore "$FUNC_PROJ"
dotnet publish "$FUNC_PROJ" -c Release -o "$OUT_DIR" --nologo

pushd "$OUT_DIR" >/dev/null
zip -q -r "$ZIP_PATH" .
popd >/dev/null

SIZE=$(du -h "$ZIP_PATH" | awk '{print $1}')
echo "[info] ZIP created: $ZIP_PATH ($SIZE)" >&2

echo "[info] Deploying ZIP to $APP in $RG" >&2
az functionapp deployment source config-zip -g "$RG" -n "$APP" --src "$ZIP_PATH" 1>/dev/null

echo "[info] Deployment submitted." >&2

if [[ $SET_BASE_URL -eq 1 ]]; then
  BASE_URL="https://$APP.azurewebsites.net"
  echo "[info] Setting Webhook__BaseUrl=$BASE_URL" >&2
  az functionapp config appsettings set -g "$RG" -n "$APP" --settings "Webhook__BaseUrl=$BASE_URL" 1>/dev/null
fi

echo "[info] Warmup call" >&2
curl -s -i "https://$APP.azurewebsites.net/api/health" || true

echo "[next] Validate notification endpoint (expect 200):" >&2
echo "curl -i 'https://$APP.azurewebsites.net/api/graph/notifications?validationToken=PING'" >&2

echo "[done] Use scripts/register-webhooks.sh afterwards." >&2
