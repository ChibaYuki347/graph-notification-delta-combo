# Azure Functions デプロイ課題と回避策

## 🚨 現在のデプロイ課題

### 概要

Azure Functions Consumption Planでのコードデプロイで以下のエラーが発生：

```text
Operation is not valid due to the current state of the object.
```

### 発生状況

- **環境**: Azure Functions Consumption Plan (Dynamic)
- **対象**: grcal-dev-func (Japan East)
- **発生コマンド**: 
  - `func azure functionapp publish grcal-dev-func`
  - `az functionapp deployment source config-zip`

### 技術的詳細

#### 1. エラーログ詳細

```text
Getting site publishing info...
[2025-09-18T08:29:45.040Z] Starting the function app deployment...
Operation is not valid due to the current state of the object.
```

#### 2. 検証した要因

✅ **正常な要素**:
- インフラストラクチャデプロイ成功
- Function App作成・起動成功
- ランタイム設定正常 (`dotnet-isolated v8.0`)
- アプリ設定正常配置
- .NETプロジェクトビルド成功

❌ **問題要素**:
- デプロイメント実行時の状態競合
- Consumption Planでの制限
- ストレージアカウントキー認証制限

#### 3. Azure設定確認結果

```bash
# Function App状態
az functionapp show -g rg-graph-cal-dev -n grcal-dev-func --query '{state:state,kind:kind,sku:sku}'
# 結果: {"kind": "functionapp,linux", "sku": "Dynamic", "state": "Running"}

# ランタイム設定
az functionapp show -g rg-graph-cal-dev -n grcal-dev-func --query 'functionAppConfig.runtime'
# 結果: {"name": "dotnet-isolated", "version": "8.0"}
```

## 💡 回避策

### 方法1: Azure Portal 手動デプロイ（推奨）

#### 手順

1. **デプロイパッケージ準備**
   ```bash
   cd FunctionApp
   dotnet publish -c Release
   cd bin/Release/net8.0/publish
   zip -r ../../deploy.zip .
   ```

2. **Portal デプロイ**
   - Azure Portal → Function App (grcal-dev-func)
   - Deployment Center → Manual Deployment
   - ZIP ファイルアップロード: `bin/Release/deploy.zip`

3. **検証**
   ```bash
   # デプロイ後の確認
   curl -i https://grcal-dev-func.azurewebsites.net/api/health
   ```

#### メリット・デメリット

**メリット**:
- ✅ 確実性が高い
- ✅ 即座に実行可能
- ✅ Portal UIでの進捗確認

**デメリット**:
- ❌ 手動作業が必要
- ❌ CI/CDパイプラインに不適
- ❌ バージョン管理が困難

### 方法2: GitHub Actions CI/CD（中期推奨）

#### セットアップ手順

1. **Azure認証情報作成**
   ```bash
   az ad sp create-for-rbac \
     --name "github-actions-grcal" \
     --role contributor \
     --scopes /subscriptions/{subscription-id}/resourceGroups/rg-graph-cal-dev \
     --sdk-auth
   ```

2. **ワークフロー作成** (`.github/workflows/deploy.yml`)
   ```yaml
   name: Deploy Azure Functions
   
   on:
     push:
       branches: [ main ]
     workflow_dispatch:
   
   jobs:
     build-and-deploy:
       runs-on: ubuntu-latest
       steps:
       - uses: actions/checkout@v3
       
       - name: Setup .NET
         uses: actions/setup-dotnet@v3
         with:
           dotnet-version: '8.0.x'
       
       - name: Build
         run: |
           cd FunctionApp
           dotnet restore
           dotnet build -c Release
           dotnet publish -c Release --output ./output
       
       - name: Deploy to Azure Functions
         uses: Azure/functions-action@v1
         with:
           app-name: grcal-dev-func
           slot-name: production
           package: ./FunctionApp/output
           publish-profile: ${{ secrets.AZURE_FUNCTIONAPP_PUBLISH_PROFILE }}
   ```

3. **認証設定**
   - GitHub Repository Settings → Secrets
   - `AZURE_CREDENTIALS`: JSON出力を追加
   - `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`: Portal から取得

#### メリット・デメリット

**メリット**:
- ✅ 完全自動化
- ✅ バージョン管理連携
- ✅ 複数環境対応容易
- ✅ ロールバック可能

**デメリット**:
- ❌ 初期設定が複雑
- ❌ GitHub Actions知識必要
- ❌ デバッグが困難な場合

### 方法3: App Service Plan変更（長期検討）

#### Premium/Dedicated Planへの移行

1. **Plan変更**
   ```bash
   # 現在のPlan確認
   az functionapp show -g rg-graph-cal-dev -n grcal-dev-func --query 'serverFarmId'
   
   # 新しいPlan作成
   az appservice plan create \
     --name grcal-premium-plan \
     --resource-group rg-graph-cal-dev \
     --sku P1V2 \
     --is-linux
   
   # Function AppをPlan移行
   az functionapp update \
     --name grcal-dev-func \
     --resource-group rg-graph-cal-dev \
     --plan grcal-premium-plan
   ```

2. **Bicep更新**
   ```bicep
   resource plan 'Microsoft.Web/serverfarms@2024-11-01' = {
     name: planName
     location: location
     sku: {
       name: 'P1V2'  // Premium Plan
       tier: 'Premium'
     }
     properties: {
       reserved: true
     }
   }
   ```

#### メリット・デメリット

**メリット**:
- ✅ デプロイオプション拡大
- ✅ パフォーマンス向上
- ✅ スケーリング柔軟性
- ✅ VNet統合可能

**デメリット**:
- ❌ コスト増加
- ❌ 過剰スペックの可能性
- ❌ 移行作業が必要

## 🔄 推奨アプローチ

### 短期（今すぐ）
1. **Azure Portal手動デプロイ**で動作確認
2. Webhookエンドポイントの疎通確認
3. 基本機能テスト実施

### 中期（1-2週間）
1. **GitHub Actions CI/CD**設定
2. ステージング環境追加
3. 自動テスト組み込み

### 長期（1ヶ月以降）
1. モニタリング・アラート設定
2. Premium Plan移行検討
3. マルチリージョン展開

## 📋 チェックリスト

### 手動デプロイ前確認

- [ ] `dotnet publish -c Release` 成功
- [ ] `deploy.zip` ファイル作成済み
- [ ] Azure Portal アクセス可能
- [ ] Function App (grcal-dev-func) 確認

### デプロイ後確認

- [ ] Function一覧表示確認
- [ ] エンドポイント応答確認
- [ ] Application Insights ログ確認
- [ ] Key Vault接続確認

### CI/CD設定後確認

- [ ] GitHub Actions ワークフロー成功
- [ ] 自動デプロイ動作確認
- [ ] ロールバック手順確認

## 🔗 関連リソース

- [Azure Functions デプロイメント技術](https://docs.microsoft.com/azure/azure-functions/functions-deployment-technologies)
- [GitHub Actions for Azure](https://github.com/Azure/actions)
- [Azure Functions Premium Plan](https://docs.microsoft.com/azure/azure-functions/functions-premium-plan)
- [Azure Portal ZIP デプロイ](https://docs.microsoft.com/azure/azure-functions/deployment-zip-push)