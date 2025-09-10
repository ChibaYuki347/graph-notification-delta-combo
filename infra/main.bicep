// =============================
// Parameters
// =============================
@description('環境識別子 (dev / stg / prod など)')
param environment string = 'dev'
@description('デプロイ地点 (location)')
param location string = resourceGroup().location
@description('Functions Consumption SKU (Y1 推奨)')
param skuName string = 'Y1'
@description('Storage SKU')
param storageSku string = 'Standard_LRS'
@description('Application Insights サンプリング率 (%)')
@minValue(0)
@maxValue(100)
param appInsightsSampling int = 10
@description('Graph Tenant Id')
param graphTenantId string
@description('Graph Client Id')
param graphClientId string
@secure()
@description('Graph Client Secret (初期投入値。enableKeyVault=false の場合は直接利用)')
param graphClientSecret string
@description('Webhook ClientState')
param webhookClientState string
@description('対象会議室 UPN カンマ区切り')
param roomsUpns string
@description('過去取得ウィンドウ(日)')
param windowDaysPast int = 3
@description('未来取得ウィンドウ(日)')
param windowDaysFuture int = 14
@description('Renew サブスクリプション CRON')
param renewCron string = '0 0 */6 * * *'
@description('Key Vault を利用しシークレットを参照するか')
param enableKeyVault bool = true
@description('Key Vault 名 (空文字で自動命名)')
param keyVaultName string = ''

// =============================
// Naming
// =============================
var baseName = toLower(replace('grcal-${environment}', '_', '-'))
var storageName = toLower(replace(uniqueString(resourceGroup().id, baseName, 'st'), '-', ''))
var aiName = '${baseName}-ai'
var planName = '${baseName}-plan'
var funcName = '${baseName}-func'
var queueNotifName = 'graph-notifications'
var queueLifecycleName = 'graph-lifecycle'
var cacheContainer = 'cache'
var stateContainer = 'state'
var metricsContainer = 'metrics'
var kvName = empty(keyVaultName) ? toLower(replace(uniqueString(resourceGroup().id, baseName, 'kv'), '-', '')) : keyVaultName

// =============================
// Resources
// =============================
// Storage
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageName
  location: location
  sku: { name: storageSku }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
  }
  tags: { 'azd-env-name': environment }
}

// 事前計算: 接続文字列 (listKeys 依存)
var storageKeys = listKeys(storage.id, '2023-01-01')
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storageKeys.keys[0].value};EndpointSuffix=core.windows.net'

// App Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: aiName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    SamplingPercentage: appInsightsSampling
  }
  tags: { 'azd-env-name': environment }
}

// Plan
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: {
    name: skuName
    tier: 'Dynamic'
  }
  properties: {
    reserved: true
  }
  tags: {
    'azd-env-name': environment
  }
}

// Key Vault (RBAC 有効 / accessPolicies なし) → 先に作成して Secrets → Function が参照
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = if (enableKeyVault) {
  name: kvName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enabledForTemplateDeployment: true
  }
  tags: {
    'azd-env-name': environment
  }
}

resource kvSecretGraphClientSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVault) {
  name: 'Graph--ClientSecret'
  parent: kv
  properties: {
    value: graphClientSecret
  }
}
resource kvSecretWebhookClientState 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (enableKeyVault) {
  name: 'Webhook--ClientState'
  parent: kv
  properties: {
    value: webhookClientState
  }
}

// Function App (v4 .NET 8 Isolated)
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: funcName
  location: location
  kind: 'functionapp,linux'
  properties: {
    httpsOnly: true
    serverFarmId: plan.id
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageConnectionString
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
          value: appInsights.properties.InstrumentationKey
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'Graph__TenantId'
          value: graphTenantId
        }
        {
          name: 'Graph__ClientId'
          value: graphClientId
        }
        {
          name: 'Graph__ClientSecret'
          value: enableKeyVault ? format('@Microsoft.KeyVault(SecretUri={0})', kvSecretGraphClientSecret.properties.secretUriWithVersion) : graphClientSecret
        }
        {
          name: 'Webhook__ClientState'
          value: enableKeyVault ? format('@Microsoft.KeyVault(SecretUri={0})', kvSecretWebhookClientState.properties.secretUriWithVersion) : webhookClientState
        }
        {
          name: 'Webhook__NotificationQueue'
          value: queueNotifName
        }
        {
          name: 'Webhook__LifecycleQueue'
          value: queueLifecycleName
        }
        {
          name: 'Rooms__Upns'
          value: roomsUpns
        }
        {
          name: 'Window__DaysPast'
          value: string(windowDaysPast)
        }
        {
          name: 'Window__DaysFuture'
          value: string(windowDaysFuture)
        }
        {
          name: 'Blob__StateContainer'
          value: stateContainer
        }
        {
          name: 'Blob__CacheContainer'
          value: cacheContainer
        }
        {
          name: 'Renew__Cron'
          value: renewCron
        }
      ]
    }
  }
  identity: { type: 'SystemAssigned' }
  tags: { 'azd-env-name': environment }
  // secrets 参照は暗黙依存があるため dependsOn 不要
}

// Key Vault RBAC Role Assignment (Secrets User)
resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableKeyVault) {
  name: guid(kv.id, functionApp.id, 'kv-secrets-user')
  scope: kv
  properties: {
    principalId: functionApp.identity.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalType: 'ServicePrincipal'
  }
}

// =============================
// Outputs
// =============================
output functionEndpoint string = 'https://${functionApp.name}.azurewebsites.net'
output storageAccountName string = storage.name
output appInsightsName string = appInsights.name
output queues object = {
  notifications: queueNotifName
  lifecycle: queueLifecycleName
}
output containers object = {
  cache: cacheContainer
  state: stateContainer
  metrics: metricsContainer
}
output keyVaultName string = enableKeyVault ? kv.name : 'disabled'
