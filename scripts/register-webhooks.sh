#!/usr/bin/env bash
set -euo pipefail

# Register (or refresh) Microsoft Graph calendar subscriptions for rooms.
# Requirements:
#  - Azure CLI logged in (for Key Vault secret retrieval if needed)
#  - curl + jq installed
#  - Environment variables or inline values below
#
# Ways to provide credentials:
# 1) Direct env vars GRAPH_TENANT_ID / GRAPH_CLIENT_ID / GRAPH_CLIENT_SECRET
# 2) Key Vault: set KEYVAULT_NAME and secret names (defaults in script)
# 3) Managed Identity (if running in Azure environment with proper perms) -> not typical for local script
#
# Usage examples:
#   ./scripts/register-webhooks.sh \
#       --function-url https://<func>.azurewebsites.net \
#       --rooms "room1@tenant.onmicrosoft.com,room2@tenant.onmicrosoft.com" \
#       --client-state <clientState>
#
#   # With Key Vault (secrets Graph--ClientSecret and Webhook--ClientState )
#   ./scripts/register-webhooks.sh \
#       --function-url https://<func>.azurewebsites.net \
#       --rooms "room1@tenant.onmicrosoft.com" \
#       --tenant-id <tenant> --client-id <appId> \
#       --keyvault <kv-name>
#
# Options:
#   --function-url    Base function endpoint (no trailing slash)
#   --rooms           Comma separated room UPNs
#   --tenant-id       Tenant (Directory) ID
#   --client-id       App (Client) ID
#   --client-secret   Client secret (if no Key Vault) OR set env GRAPH_CLIENT_SECRET
#   --client-state    Webhook clientState (if no Key Vault)
#   --keyvault        Key Vault name to resolve secrets
#   --secret-name     Graph client secret name (default Graph--ClientSecret)
#   --state-secret    Webhook client state secret name (default Webhook--ClientState)
#   --dry-run         Only print request bodies (no Graph API call)
#   --retries         Validation 失敗時の再試行回数 (default 2)
#   --delay-ms        各リクエスト間遅延 (ms, default 500)
#   --help            Show help

SECRET_NAME=Graph--ClientSecret
STATE_SECRET=Webhook--ClientState

print_help(){ sed -n '1,/^print_help/d;/^EOF/,$d' "$0"; cat <<'EOF'
EOF
}

# Parse args
while [[ $# -gt 0 ]]; do
  case "$1" in
    --function-url) FUNCTION_URL="$2"; shift;;
    --rooms) ROOMS_CSV="$2"; shift;;
    --tenant-id) GRAPH_TENANT_ID="$2"; shift;;
    --client-id) GRAPH_CLIENT_ID="$2"; shift;;
    --client-secret) GRAPH_CLIENT_SECRET="$2"; shift;;
    --client-state) WEBHOOK_CLIENT_STATE="$2"; shift;;
    --keyvault) KEYVAULT_NAME="$2"; shift;;
    --secret-name) SECRET_NAME="$2"; shift;;
    --state-secret) STATE_SECRET="$2"; shift;;
  --dry-run) DRY_RUN=1;;
  --retries) RETRIES="$2"; shift;;
  --delay-ms) DELAY_MS="$2"; shift;;
    --help|-h) print_help; exit 0;;
    *) echo "Unknown arg: $1" >&2; exit 1;;
  esac
  shift
done

if [[ -z "${FUNCTION_URL:-}" || -z "${ROOMS_CSV:-}" ]]; then
  echo "--function-url と --rooms は必須です" >&2; exit 1
fi

if [[ -n "${KEYVAULT_NAME:-}" ]]; then
  echo "[info] Key Vault からシークレット取得" >&2
  GRAPH_CLIENT_SECRET=${GRAPH_CLIENT_SECRET:-$(az keyvault secret show --vault-name "$KEYVAULT_NAME" -n "$SECRET_NAME" --query value -o tsv)}
  WEBHOOK_CLIENT_STATE=${WEBHOOK_CLIENT_STATE:-$(az keyvault secret show --vault-name "$KEYVAULT_NAME" -n "$STATE_SECRET" --query value -o tsv)}
fi

: "${GRAPH_TENANT_ID:?--tenant-id 必須 (または env GRAPH_TENANT_ID) }"
: "${GRAPH_CLIENT_ID:?--client-id 必須 (または env GRAPH_CLIENT_ID) }"
: "${GRAPH_CLIENT_SECRET:?--client-secret 必須 または Key Vault 指定}" 
: "${WEBHOOK_CLIENT_STATE:?--client-state 必須 または Key Vault 指定}" 

if [[ -z "${DRY_RUN:-}" ]]; then
  # Acquire token (skip in dry-run)
  TOKEN=$(curl -s -X POST "https://login.microsoftonline.com/$GRAPH_TENANT_ID/oauth2/v2.0/token" \
    -H 'Content-Type: application/x-www-form-urlencoded' \
    -d "client_id=$GRAPH_CLIENT_ID&scope=https%3A%2F%2Fgraph.microsoft.com%2F.default&client_secret=$GRAPH_CLIENT_SECRET&grant_type=client_credentials" | jq -r '.access_token')

  if [[ -z "$TOKEN" || "$TOKEN" == "null" ]]; then
    echo "トークン取得失敗" >&2; exit 1
  fi
else
  echo "[dry-run] Graph API 呼び出しをスキップします" >&2
fi

NOTIFICATION_URL="${FUNCTION_URL%/}/api/graph/notifications"
EXPIRES=$(date -u -v+6d '+%Y-%m-%dT%H:%M:%SZ' 2>/dev/null || date -u -d '+6 days' '+%Y-%m-%dT%H:%M:%SZ')

IFS=',' read -r -a ROOMS <<< "$ROOMS_CSV"
RESULTS=()
RETRIES=${RETRIES:-2}
DELAY_MS=${DELAY_MS:-500}

for ROOM in "${ROOMS[@]}"; do
  ROOM_TRIM=$(echo "$ROOM" | awk '{$1=$1};1')
  echo "[info] Creating subscription for $ROOM_TRIM" >&2
  # Microsoft Graph REST API は camelCase のプロパティ名を要求する
  BODY=$(jq -n \
    --arg ct "created,updated,deleted" \
    --arg url "$NOTIFICATION_URL" \
    --arg res "/users/$ROOM_TRIM/events" \
    --arg cs "$WEBHOOK_CLIENT_STATE" \
    --arg exp "$EXPIRES" '{changeType:$ct,notificationUrl:$url,resource:$res,clientState:$cs,expirationDateTime:$exp}')

  if [[ "${DEBUG:-}" == "1" ]]; then
    echo "[debug] request body: $(echo "$BODY" | jq -c '.')" >&2
  fi

  if [[ -n "${DRY_RUN:-}" ]]; then
    echo "[dry-run] BODY for $ROOM_TRIM: $(echo "$BODY" | jq -c '.')" >&2
    RESULTS+=("{\"room\":\"$ROOM_TRIM\",\"dryRun\":true}")
  else
    attempt=0
    success=0
    while (( attempt <= RETRIES )); do
      [[ $attempt -gt 0 ]] && sleep "$(bc <<< "scale=3;$DELAY_MS/1000")"
      RESP=$(curl -s -D - -o /tmp/sub_resp.json -X POST https://graph.microsoft.com/v1.0/subscriptions \
        -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "$BODY")
      STATUS=$(echo "$RESP" | awk 'NR==1{print $2}')
      if [[ "$STATUS" =~ ^2 ]]; then
        ID=$(jq -r '.id' /tmp/sub_resp.json)
        EXP=$(jq -r '.expirationDateTime' /tmp/sub_resp.json)
        RESULTS+=("{\"room\":\"$ROOM_TRIM\",\"subscriptionId\":\"$ID\",\"expiration\":\"$EXP\",\"attempt\":$attempt}")
        success=1
        break
      else
        MSG=$(cat /tmp/sub_resp.json | jq -r '.error.message? // "unknown"')
        if [[ $attempt -lt RETRIES ]]; then
          echo "[warn] attempt=$attempt status=$STATUS room=$ROOM_TRIM retrying..." >&2
        fi
      fi
      ((attempt++))
    done
    if (( success==0 )); then
      MSG=$(cat /tmp/sub_resp.json | jq -r '.error.message? // "unknown"')
      RESULTS+=("{\"room\":\"$ROOM_TRIM\",\"error\":\"$STATUS\",\"message\":\"$MSG\",\"attempts\":$attempt}")
    fi
    rm -f /tmp/sub_resp.json
  fi
  # inter-room pacing
  sleep "$(bc <<< "scale=3;$DELAY_MS/1000")"
done

JSON=$(printf '%s\n' "${RESULTS[@]}" | jq -s --arg notificationUrl "$NOTIFICATION_URL" --arg dry "${DRY_RUN:-}" '{notificationUrl:$notificationUrl, dryRun:($dry=="1"), count:(length), results:.}')

echo "$JSON" | jq '.'
