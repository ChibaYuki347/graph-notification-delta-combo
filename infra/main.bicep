@description('環境識別子 (例: dev, staging, prod)')
param environment string = 'dev'
@description('地域')
param location string = resourceGroup().location
@description('Azure Functions 用ホスト名プリフィックス')
param functionAppName string = uniqueString(resourceGroup().id, environment, 'graphfn')
@description('App Service プランSKU (従量課金: Y1)')
param skuName string = 'Y1'
@description('Storage アカウントSKU')
param storageSku string = 'Standard_LRS'
@description('Application Insights サンプリング率')
@minValue(0)
@maxValue(100)
param appInsightsSampling int = 10
@description('Graph Tenant Id')
param graphTenantId string
@description('Graph Client Id')
param graphClientId string
@secure()
@description('Graph Client Secret (KeyVault未利用の簡易PoC)')
param graphClientSecret string
@description('Webhook クライアントステート')
param webhookClientState string
@description('対象会議室UPN カンマ区切り')
param roomsUpns string
@description('過去何日遡るか')
param windowDaysPast int = 3
@description('未来何日先まで')
param windowDaysFuture int = 14
@description('Renew サイクルCRON')
param renewCron string = '0 0 */6 * * *'

// 名前生成
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
}

// Queues & Containers (deploymentScripts を使わず ARM拡張機能) - queue/container は後で Function 起動時に自動作成されるため明示作成不要。必要なら Storage 管理ライブラリ。ここでは出力のみ。

// App Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: aiName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    SamplingPercentage: appInsightsSampling
  }
}

// Consumption Plan (Linux)
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
}

// Function App (v4 isolated .NET 8)
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
        { name: 'AzureWebJobsStorage'; value: storageListKeys.outputs.connectionString }
        { name: 'FUNCTIONS_WORKER_RUNTIME'; value: 'dotnet-isolated' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE'; value: '1' }
        { name: 'APPINSIGHTS_INSTRUMENTATIONKEY'; value: appInsights.properties.InstrumentationKey }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'; value: appInsights.properties.ConnectionString }
        { name: 'Graph__TenantId'; value: graphTenantId }
        { name: 'Graph__ClientId'; value: graphClientId }
        { name: 'Graph__ClientSecret'; value: graphClientSecret }
        { name: 'Webhook__ClientState'; value: webhookClientState }
        { name: 'Webhook__NotificationQueue'; value: queueNotifName }
        { name: 'Webhook__LifecycleQueue'; value: queueLifecycleName }
        { name: 'Rooms__Upns'; value: roomsUpns }
        { name: 'Window__DaysPast'; value: string(windowDaysPast) }
        { name: 'Window__DaysFuture'; value: string(windowDaysFuture) }
        { name: 'Blob__StateContainer'; value: stateContainer }
        { name: 'Blob__CacheContainer'; value: cacheContainer }
        { name: 'Renew__Cron'; value: renewCron }
      ]
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
  tags: {
    'azd-env-name': environment
  }
}

// Storage connection string を取得するため ListKeys 呼び出し (モジュール化簡易)
resource storageListKeys 'Microsoft.Storage/storageAccounts/listKeys@2023-01-01' = {
  name: 'listKeys'
  parent: storage
  properties: {
    expand: ''
  }
}

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
