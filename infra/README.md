# インフラ (Bicep)

このフォルダは PoC アプリ (Graph 通知 + Delta + Blob キャッシュ) を Azure にデプロイする最小構成の Bicep テンプレートを提供します。

## 含まれるリソース

- Storage Account (V2, LRS)
- Application Insights
- (Linux) Consumption Plan (Y1)
- Azure Function App (.NET 8 Isolated)
- キュー/Blob コンテナは Function 実行時に自動作成 (環境変数で名前指定)。

## パラメータ

| 名前 | 説明 | 既定値 |
|------|------|--------|
| environment | 環境識別 dev/stg/prod等 | dev |
| location | デプロイ地域 | resourceGroup().location |
| functionAppName | (任意) 指定しなければ uniqueString |  |
| skuName | Functions SKU | Y1 |
| storageSku | Storage SKU | Standard_LRS |
| appInsightsSampling | AI サンプリング率 | 10 |
| graphTenantId | Graph テナント ID | (必須) |
| graphClientId | Graph アプリ (ClientId) | (必須) |
| graphClientSecret | Graph アプリ シークレット | (必須/secure) |
| webhookClientState | 通知検証用シークレット | (必須) |
| roomsUpns | 監視対象会議室 UPN カンマ区切り | (必須) |
| windowDaysPast | 取得対象過去日数 | 3 |
| windowDaysFuture | 取得対象未来日数 | 14 |
| renewCron | サブスクリプション更新 CRON | `0 0 */6 * * *` |
| enableKeyVault | Key Vault でシークレット参照 (true 推奨) | true |
| keyVaultName | 省略時は自動命名 | '' |

## デプロイ手順 (例)

```bash
# リソースグループ
RG=rg-graph-cal-dev
LOCATION=japaneast
az group create -n $RG -l $LOCATION

# パラメータ値 (例)
TENANT_ID="<tenant-guid>"
CLIENT_ID="<app-client-id>"
CLIENT_SECRET="<secret>"
CLIENT_STATE="<long-random-string>"
ROOMS="ConfRoom1@xxx.onmicrosoft.com,ConfRoom2@xxx.onmicrosoft.com"

# デプロイ
az deployment group create \
  -g $RG \
  -f infra/main.bicep \
  -p graphTenantId=$TENANT_ID graphClientId=$CLIENT_ID graphClientSecret=$CLIENT_SECRET \
     webhookClientState=$CLIENT_STATE roomsUpns="$ROOMS" environment=dev

# もしくはパラメータファイル + スクリプト
chmod +x infra/deploy.sh
./infra/deploy.sh $RG $LOCATION infra/parameters.dev.json
```

## デプロイ後

1. `functionEndpoint` 出力を控える。
2. ローカル `REACT_APP_FUNCTION_BASE_URL` をその URL に変更 (フロントエンド再ビルド)。
3. Graph Webhook を Azure 上の Function URL で再登録 (PoC の場合 ngrok -> Azure に切替)。
4. 動作確認: `/api/rooms/{roomUPN}/events` が 200 を返すこと。

## Key Vault 利用について

`enableKeyVault=true` の場合:

1. Key Vault (RBAC) を作成 (accessPolicies なし / enableRbacAuthorization=true)
2. 2 つのシークレット `Graph--ClientSecret`, `Webhook--ClientState` を投入
3. Function App のアプリ設定で `@Microsoft.KeyVault(SecretUri=...)` 参照を設定
4. Function のシステム割当マネージドIDに "Key Vault Secrets User" ロールを付与

注意:

- テンプレート出力で Secret URI は返却していません (セキュリティ簡素化)。必要なら別途 `az keyvault secret show` で取得。
- 将来的にローテーションを行う場合は Key Vault のバージョン管理で対応可能。

`enableKeyVault=false` の場合: 平文で Function App 設定に格納 (PoC/検証向け)。

## 今後の拡張候補

- Key Vault + Managed Identity で Graph シークレット管理
- Storage キュー/コンテナを Bicep で明示作成 (module / extensionResource)
- CDN / Static Web Apps を使ったフロントエンドホスティング
- Azure Monitor Workbook によるレイテンシ可視化
- Terraform 版 IaC

---

PoC 用の最小構成のためセキュリティ強化 (IP 制限, VNET など) は別途検討してください。
