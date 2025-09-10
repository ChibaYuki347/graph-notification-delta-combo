#!/usr/bin/env bash
set -euo pipefail

echo "[preflight] Azure CLI ログイン確認" >&2
if ! az account show >/dev/null 2>&1; then
  echo "Azure CLI にログインしていません: az login を実行してください" >&2
  exit 1
fi

echo "[preflight] サブスクリプション選択確認" >&2
SUB_ID=$(az account show --query id -o tsv)
echo "  Subscription: $SUB_ID" >&2

echo "[preflight] 必要なプロバイダ登録状態確認" >&2
for ns in Microsoft.Storage Microsoft.Web Microsoft.Insights Microsoft.KeyVault; do
  state=$(az provider show -n "$ns" --query registrationState -o tsv)
  echo "  $ns: $state"
  if [[ "$state" != "Registered" ]]; then
    echo "    -> 登録中でないため登録要求: az provider register -n $ns" >&2
    az provider register -n "$ns" >/dev/null &
  fi
done
wait || true

echo "[preflight] Bicep コンパイル検証" >&2
if ! az bicep version >/dev/null 2>&1; then
  echo "bicep CLI が az に統合されていません。最新の Azure CLI へ更新を推奨" >&2
else
  az bicep build -f infra/main.bicep -o /tmp/main.json >/dev/null
  echo "  -> OK"
fi

PARAM_FILE=${1:-infra/parameters.dev.local.json}
if [[ ! -f "$PARAM_FILE" ]]; then
  echo "パラメータファイル $PARAM_FILE が存在しません" >&2
  exit 1
fi

echo "[preflight] シークレット露出確認 (ローカル専用 param 判定)" >&2
if grep -qi "MP8Q" "$PARAM_FILE"; then
  echo "  注意: 実シークレットらしき値が含まれています。コミットしないでください。" >&2
fi

echo "[preflight] パラメータ JSON スキーマ簡易検証" >&2
jq . "$PARAM_FILE" >/dev/null

echo "[preflight] 完了: デプロイ前チェック OK (what-if は RG 指定後に実行可能)" >&2