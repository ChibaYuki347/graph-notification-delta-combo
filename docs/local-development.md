# ローカル開発・検証手順

## 🛠️ 開発環境セットアップ

### 必要なツール

```bash
# Homebrew (macOS)
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
eval "$(/opt/homebrew/bin/brew shellenv)"

# Azure CLI
brew install azure-cli

# .NET SDK 8.0
brew install dotnet
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"

# Node.js (UIフロントエンド用)
brew install node

# Azurite (Azure Storage エミュレータ)
npm install -g azurite

# ngrok (HTTPS トンネリング - Webhook開発用)
brew install ngrok/ngrok/ngrok

# Azure Functions Core Tools (通常は自動インストール済み)
# brew install azure-functions-core-tools@4
```

### Azure認証

```bash
# Azureにログイン
az login

# サブスクリプション確認
az account show

# 必要に応じてサブスクリプション変更
az account set --subscription "your-subscription-id"
```

### ngrok認証（Webhook開発用）

```bash
# ngrokアカウント作成後、認証トークン設定
ngrok config add-authtoken <your-authtoken>

# 認証状態確認
ngrok config check
```

### Azurite（Azure Storage エミュレータ）

```bash
# Azurite起動（デフォルトポート使用）
azurite --silent --location ./azurite --debug ./azurite/debug.log

# 別ターミナルで確認
az storage account list --query "[?name=='devstoreaccount1']"
```

## 🏗️ プロジェクトビルド

### ローカルビルド

```bash
cd FunctionApp

# パッケージ復元とビルド
dotnet restore
dotnet build

# リリースビルド
dotnet build -c Release

# パブリッシュ（デプロイ用）
dotnet publish -c Release
```

### ローカル実行

#### フル開発環境（Webhook対応）

```bash
# ターミナル1: Azurite起動
azurite --silent --location ./azurite --debug ./azurite/debug.log

# ターミナル2: Azure Functions API起動
cd FunctionApp
func start

# ターミナル3: ngrok HTTPS トンネル
ngrok http 7071

# ターミナル4: React UI起動
cd ui/room-calendar
npm start

# ngrokで表示されたHTTPS URLをWebhook設定に使用
# 例: https://abc123.ngrok.io → Webhook__BaseUrl
```

#### Azure Functions (バックエンドのみ)

```bash
# Functions Core Toolsでローカル実行
func start

# 別ターミナルでHealthCheck
curl http://localhost:7071/api/health
```

#### React UI (フロントエンドのみ)

```bash
# UIディレクトリに移動
cd ui/room-calendar

# 依存関係インストール（初回のみ）
npm install

# 開発サーバー起動
npm start

# ブラウザで自動的に開かれる
# http://localhost:3000
```

#### 統合テスト環境

```bash
# ターミナル1: Functions API起動
cd FunctionApp
func start

# ターミナル2: React UI起動
cd ui/room-calendar
npm start

# ブラウザでUIからAPI連携テスト
# http://localhost:3000 → http://localhost:7071/api/*
```

## 🔗 Webhook設定（ローカル開発）

### ngrok URL取得

```bash
# ngrok起動後、Public URLを確認
ngrok http 7071

# 出力例:
# Forwarding  https://abc123.ngrok.io -> http://localhost:7071
```

### 環境変数設定

`local.settings.json`でngrok URLを設定：

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "Webhook__BaseUrl": "https://abc123.ngrok.io",
    "GraphApi__ClientId": "your-client-id",
    "GraphApi__ClientSecret": "your-client-secret",
    "GraphApi__TenantId": "your-tenant-id"
  }
}
```

### Graph API Subscription設定

Microsoft Graph APIでWebhook URLを登録：

```bash
# 例: カレンダー変更通知
POST https://graph.microsoft.com/v1.0/subscriptions
{
  "changeType": "created,updated,deleted",
  "notificationUrl": "https://abc123.ngrok.io/api/notifications",
  "resource": "/me/events",
  "expirationDateTime": "2024-12-31T23:59:59.0000000Z"
}
```

### Webhook テスト

```bash
# 通知エンドポイントの確認
curl -X POST https://abc123.ngrok.io/api/notifications \
  -H "Content-Type: application/json" \
  -d '{"test": "webhook"}'

# ngrok Web UIでリクエスト詳細確認
# http://localhost:4040
```

## 🧪 テスト・検証

### 単体テスト

#### Azure Functions (.NET)

```bash
# テストプロジェクトがある場合
dotnet test
```

#### React UI (TypeScript)

```bash
cd ui/room-calendar

# Jest単体テスト実行
npm test

# カバレッジ付きテスト
npm test -- --coverage

# テストファイルの変更を監視
npm test -- --watch
```

### 統合テスト（ローカル）

```bash
# Mock Graph APIを使用したテスト
# TODO: テスト環境セットアップスクリプト作成
```

### Azure環境でのテスト

```bash
# Function App状態確認
az functionapp show -g rg-graph-cal-dev -n grcal-dev-func --query '{state:state,runtime:functionAppConfig.runtime}'

# ログ確認
az webapp log tail -g rg-graph-cal-dev -n grcal-dev-func

# 特定のFunction実行テスト
curl -X POST "https://grcal-dev-func.azurewebsites.net/api/subscribe-rooms" \
  -H "Content-Type: application/json" \
  -d '{"test": true}'
```

## 📦 デプロイパッケージ作成

### 手動パッケージ作成

```bash
cd FunctionApp

# Releaseビルド
dotnet publish -c Release

# ZIPパッケージ作成
cd bin/Release/net8.0/publish
zip -r ../../deploy.zip .

# パッケージ確認
ls -la bin/Release/deploy.zip
```

### デプロイ検証

```bash
# パッケージサイズ確認
ls -lh bin/Release/deploy.zip

# 必要なファイルが含まれているか確認
unzip -l bin/Release/deploy.zip | grep -E "(FunctionApp|functions.metadata|host.json)"
```

## 🔧 設定ファイル管理

### local.settings.json

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "Graph__TenantId": "your-tenant-id",
    "Graph__ClientId": "your-client-id",
    "Graph__ClientSecret": "your-client-secret",
    "Webhook__ClientState": "your-client-state",
    "Rooms__Upns": "room1@domain.com,room2@domain.com"
  }
}
```

### 環境別パラメータ

```bash
# 開発環境
infra/parameters.dev.local.json

# 本番環境（将来）
infra/parameters.prod.json
```

## 🐛 トラブルシューティング

### よくある問題

1. **Azure Functions ビルドエラー**

   ```bash
   # NuGetキャッシュクリア
   dotnet nuget locals all --clear
   dotnet restore --force
   ```

2. **React UI ビルドエラー**

   ```bash
   cd ui/room-calendar
   
   # node_modules削除・再インストール
   rm -rf node_modules package-lock.json
   npm install
   
   # TypeScriptエラー確認
   npm run build
   ```

3. **CORS エラー (UI → API)**

   ```bash
   # Functions host.json でCORS設定確認
   # または開発時プロキシ設定
   ```

2. **ランタイムエラー**
   ```bash
   # ターゲットフレームワーク確認
   dotnet --list-runtimes
   
   # プロジェクトファイル確認
   cat FunctionApp.csproj | grep TargetFramework
   ```

3. **Azure接続エラー**
   ```bash
   # Azure認証状態確認
   az account show
   
   # トークン更新
   az account get-access-token
   ```

### ログ確認方法

```bash
# ローカル実行時のログ
func start --verbose

# Azure環境のログ
az webapp log tail -g rg-graph-cal-dev -n grcal-dev-func

# Application Insightsでの詳細分析
az monitor app-insights query \
  --app grcal-dev-ai \
  --analytics-query "requests | limit 10"
```

## 📊 パフォーマンス監視

### メトリクス確認

```bash
# Function実行統計
az functionapp function show \
  -g rg-graph-cal-dev \
  -n grcal-dev-func \
  --function-name DeltaWorkerFunction

# Application Insightsメトリクス
az monitor metrics list \
  --resource /subscriptions/{subscription-id}/resourceGroups/rg-graph-cal-dev/providers/Microsoft.Insights/components/grcal-dev-ai
```

### リソース使用量

```bash
# ストレージ使用量
az storage account show-usage \
  --account-name zvz6xrjkrv7qi

# Key Vault使用状況
az keyvault secret list \
  --vault-name uriihk26ytbeu
```

## 🔄 継続的インテグレーション

### GitHub Actions準備

```bash
# Azure認証情報取得（CI/CD用）
az ad sp create-for-rbac \
  --name "github-actions-grcal" \
  --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/rg-graph-cal-dev \
  --sdk-auth
```

### ワークフロー検証

```bash
# GitHub Actionsローカル実行（act使用）
# brew install act
act -j build
```