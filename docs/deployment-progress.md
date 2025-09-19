# Azure Functions デプロイ進捗レポート

## 📋 概要

Microsoft Graph Calendar Notification システムのAzure Functions環境へのデプロイ作業の進捗状況をまとめたドキュメントです。

**作業日時**: 2025年9月18日-19日  
**対象環境**: Azure Japan East (rg-graph-cal-dev)  
**Function App名**: grcal-dev-func

## ✅ 完了した作業

### 1. インフラストラクチャのデプロイ
- **問題**: `.NET version is missing or invalid` エラー
- **原因**: Bicepテンプレートでの古いランタイム設定（`siteConfig.linuxFxVersion`）
- **解決**: `functionAppConfig`構造への移行
  ```bicep
  functionAppConfig: {
    runtime: {
      name: 'dotnet-isolated'
      version: '8.0'
    }
  }
  ```

### 2. Consumption Plan制限への対応
- **問題**: `FUNCTIONS_WORKER_RUNTIME is invalid for Flex Consumption sites`
- **解決**: アプリ設定から`FUNCTIONS_WORKER_RUNTIME`を削除

### 3. デプロイストレージ設定
- **問題**: `Site.FunctionAppConfig.Deployment.Storage is invalid`
- **解決**: デプロイメントストレージ設定の追加
  ```bicep
  deployment: {
    storage: {
      type: 'blobContainer'
      value: '${storage.properties.primaryEndpoints.blob}app-package-${funcName}'
      authentication: {
        type: 'SystemAssignedIdentity'
      }
    }
  }
  ```

### 4. 開発環境のセットアップ
- ✅ Homebrew, Azure CLI, .NET SDK 8.0のインストール
- ✅ Azure Functions Core Tools (v4.2.2) の確認
- ✅ FunctionAppプロジェクトのビルド成功

## ⚠️ 現在の課題

### デプロイメント実行時のエラー
**エラー内容**: 
```
Operation is not valid due to the current state of the object.
```

**発生コマンド**:
- `func azure functionapp publish grcal-dev-func`
- `az functionapp deployment source config-zip`

**推定原因**:
- Consumption Planでのデプロイメント制限
- Function App状態との競合
- ストレージアカウントのキーベース認証制限

## 🎯 インフラストラクチャ検証結果

### Function App設定確認
```bash
# ランタイム設定（✅ 正常）
az functionapp show -g rg-graph-cal-dev -n grcal-dev-func --query 'functionAppConfig.runtime'
# 結果: {"name": "dotnet-isolated", "version": "8.0"}

# エンドポイント確認（✅ 応答）
curl -i 'https://grcal-dev-func.azurewebsites.net/api/warmup'
# 結果: HTTP/1.1 404 Not Found (コードデプロイ前なので正常)
```

### リソース状況
- ✅ Storage Account: zvz6xrjkrv7qi
- ✅ Key Vault: uriihk26ytbeu  
- ✅ Application Insights: grcal-dev-ai
- ✅ Function App: grcal-dev-func (Running状態)

## 📁 プロジェクト構成

```
FunctionApp/                        # Azure Functions バックエンド
├── Functions/
│   ├── DeltaWorkerFunction.cs      # Graph Delta処理
│   ├── NotificationsFunction.cs    # Webhook受信
│   ├── RenewSubscriptionsFunction.cs # サブスクリプション更新
│   ├── SubscribeRoomsFunction.cs   # 初期サブスクリプション
│   ├── HealthFunction.cs           # ヘルスチェック
│   └── WarmupFunction.cs           # ウォームアップ
├── Models/
│   ├── GraphChangeNotification.cs  # Graph通知モデル
│   └── QueueMessages.cs           # キューメッセージ
├── Services/
│   ├── BlobEventCacheStore.cs     # イベントキャッシュ
│   ├── BlobStateStore.cs          # 状態管理
│   ├── IEventCacheStore.cs        # インターフェース
│   └── IStateStore.cs             # インターフェース
├── Utils/
│   ├── GraphHelpers.cs            # Graph API ヘルパー
│   └── VisitorIdExtractor.cs      # 来訪者ID抽出
├── FunctionApp.csproj             # プロジェクト設定
├── host.json                      # Functions ホスト設定
├── local.settings.json            # ローカル設定
└── Program.cs                     # エントリーポイント

ui/room-calendar/                   # React フロントエンド
├── src/
│   ├── components/                # Reactコンポーネント
│   ├── services/                  # API通信ロジック
│   ├── types/                     # TypeScript型定義
│   └── App.tsx                    # メインアプリ
├── public/                        # 静的ファイル
├── package.json                   # Node.js依存関係
└── tsconfig.json                  # TypeScript設定

infra/                             # Infrastructure as Code
├── main.bicep                     # Azure リソース定義
└── parameters.dev.local.json     # 開発環境パラメータ

docs/                              # プロジェクトドキュメント
├── deployment-progress.md         # デプロイ進捗レポート
├── local-development.md           # ローカル開発手順
└── deployment-issues.md           # デプロイ課題・解決策
```

## 🔄 次のステップ

### 短期対応（手動デプロイ）
1. **Azure Portal経由でのZIPデプロイ**
   - 作成済みのZIPファイル: `bin/Release/deploy.zip`
   - Portal > Function App > Deployment Center > Manual Deployment

### 中期対応（自動化）
1. **GitHub Actions CI/CD設定**
   - ワークフロー作成
   - Azure資格情報の設定
   - 自動ビルド・デプロイ

### 長期対応（最適化）
1. **Plan変更検討**
   - Consumption → Premium/Dedicated Plan
   - より柔軟なデプロイオプション

## 📚 関連ファイル

- `infra/main.bicep` - インフラストラクチャ定義
- `infra/parameters.dev.local.json` - 開発環境パラメータ  
- `docs/portal-validation.md` - ポータル生成ARMテンプレート比較
- `FunctionApp/bin/Release/deploy.zip` - デプロイ用パッケージ

## 🔗 有用なコマンド

```bash
# インフラ再デプロイ
az deployment group create -g rg-graph-cal-dev -f infra/main.bicep -p @infra/parameters.dev.local.json

# Function App状態確認
az functionapp show -g rg-graph-cal-dev -n grcal-dev-func --query '{state:state,runtime:functionAppConfig.runtime}'

# ローカルビルド
dotnet build
dotnet publish -c Release

# デプロイパッケージ作成
cd bin/Release/net8.0/publish && zip -r ../../deploy.zip .
```