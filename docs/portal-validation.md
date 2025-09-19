# ポータルでの検証結果

Bicepで.NET Runtime 8 Isolatedがうまくデプロイされないためポータルで実行し差分を確認。

## ARM Template

```json
{
    "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
    "contentVersion": "1.0.0.0",
    "parameters": {
        "sites_grcal_dev_func_portal_name": {
            "defaultValue": "grcal-dev-func-portal",
            "type": "String"
        },
        "serverfarms_ASP_grcaldevfuncportalgroup_97b9_externalid": {
            "defaultValue": "/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Web/serverfarms/ASP-grcaldevfuncportalgroup-97b9",
            "type": "String"
        },
        "userAssignedIdentities_grcal_dev_func_portal_uami_externalid": {
            "defaultValue": "/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.ManagedIdentity/userAssignedIdentities/grcal-dev-func-portal-uami",
            "type": "String"
        }
    },
    "variables": {},
    "resources": [
        {
            "type": "Microsoft.Web/sites",
            "apiVersion": "2024-11-01",
            "name": "[parameters('sites_grcal_dev_func_portal_name')]",
            "location": "Japan East",
            "tags": {
                "hidden-link: /app-insights-resource-id": "/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Insights/components/grcal-dev-func-portal"
            },
            "kind": "functionapp,linux",
            "identity": {
                "type": "UserAssigned",
                "userAssignedIdentities": {
                    "/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourcegroups/grcal-dev-func-portal_group/providers/Microsoft.ManagedIdentity/userAssignedIdentities/grcal-dev-func-portal-uami": {}
                }
            },
            "properties": {
                "enabled": true,
                "hostNameSslStates": [
                    {
                        "name": "[concat(parameters('sites_grcal_dev_func_portal_name'), '-a3f0h5eygzced4fk.japaneast-01.azurewebsites.net')]",
                        "sslState": "Disabled",
                        "hostType": "Standard"
                    },
                    {
                        "name": "[concat(parameters('sites_grcal_dev_func_portal_name'), '-a3f0h5eygzced4fk.scm.japaneast-01.azurewebsites.net')]",
                        "sslState": "Disabled",
                        "hostType": "Repository"
                    }
                ],
                "serverFarmId": "[parameters('serverfarms_ASP_grcaldevfuncportalgroup_97b9_externalid')]",
                "reserved": true,
                "isXenon": false,
                "hyperV": false,
                "dnsConfiguration": {},
                "outboundVnetRouting": {
                    "allTraffic": false,
                    "applicationTraffic": false,
                    "contentShareTraffic": false,
                    "imagePullTraffic": false,
                    "backupRestoreTraffic": false
                },
                "siteConfig": {
                    "numberOfWorkers": 1,
                    "acrUseManagedIdentityCreds": false,
                    "alwaysOn": false,
                    "http20Enabled": false,
                    "functionAppScaleLimit": 100,
                    "minimumElasticInstanceCount": 0
                },
                "functionAppConfig": {
                    "deployment": {
                        "storage": {
                            "type": "blobcontainer",
                            "value": "[concat('https://grcaldevfuncportalgbea3.blob.core.windows.net/app-package-', parameters('sites_grcal_dev_func_portal_name'), '-4cd7659')]",
                            "authentication": {
                                "type": "userassignedidentity",
                                "userAssignedIdentityResourceId": "[parameters('userAssignedIdentities_grcal_dev_func_portal_uami_externalid')]"
                            }
                        }
                    },
                    "runtime": {
                        "name": "dotnet-isolated",
                        "version": "8.0"
                    },
                    "scaleAndConcurrency": {
                        "maximumInstanceCount": 100,
                        "instanceMemoryMB": 2048
                    }
                },
                "scmSiteAlsoStopped": false,
                "clientAffinityEnabled": false,
                "clientAffinityProxyEnabled": false,
                "clientCertEnabled": false,
                "clientCertMode": "Required",
                "hostNamesDisabled": false,
                "ipMode": "IPv4",
                "customDomainVerificationId": "0DA736A64E9249F296A477BF0D086832D4B90832AD3670A3AB0C1FCB2E14ACE4",
                "containerSize": 1536,
                "dailyMemoryTimeQuota": 0,
                "httpsOnly": true,
                "endToEndEncryptionEnabled": false,
                "redundancyMode": "None",
                "publicNetworkAccess": "Enabled",
                "storageAccountRequired": false,
                "keyVaultReferenceIdentity": "SystemAssigned",
                "autoGeneratedDomainNameLabelScope": "TenantReuse"
            }
        },
        {
            "type": "Microsoft.Web/sites/basicPublishingCredentialsPolicies",
            "apiVersion": "2024-11-01",
            "name": "[concat(parameters('sites_grcal_dev_func_portal_name'), '/ftp')]",
            "location": "Japan East",
            "dependsOn": [
                "[resourceId('Microsoft.Web/sites', parameters('sites_grcal_dev_func_portal_name'))]"
            ],
            "tags": {
                "hidden-link: /app-insights-resource-id": "/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Insights/components/grcal-dev-func-portal"
            },
            "properties": {
                "allow": false
            }
        },
        {
            "type": "Microsoft.Web/sites/basicPublishingCredentialsPolicies",
            "apiVersion": "2024-11-01",
            "name": "[concat(parameters('sites_grcal_dev_func_portal_name'), '/scm')]",
            "location": "Japan East",
            "dependsOn": [
                "[resourceId('Microsoft.Web/sites', parameters('sites_grcal_dev_func_portal_name'))]"
            ],
            "tags": {
                "hidden-link: /app-insights-resource-id": "/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Insights/components/grcal-dev-func-portal"
            },
            "properties": {
                "allow": false
            }
        },
        {
            "type": "Microsoft.Web/sites/config",
            "apiVersion": "2024-11-01",
            "name": "[concat(parameters('sites_grcal_dev_func_portal_name'), '/web')]",
            "location": "Japan East",
            "dependsOn": [
                "[resourceId('Microsoft.Web/sites', parameters('sites_grcal_dev_func_portal_name'))]"
            ],
            "tags": {
                "hidden-link: /app-insights-resource-id": "/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Insights/components/grcal-dev-func-portal"
            },
            "properties": {
                "numberOfWorkers": 1,
                "defaultDocuments": [
                    "Default.htm",
                    "Default.html",
                    "Default.asp",
                    "index.htm",
                    "index.html",
                    "iisstart.htm",
                    "default.aspx",
                    "index.php"
                ],
                "netFrameworkVersion": "v4.0",
                "requestTracingEnabled": false,
                "remoteDebuggingEnabled": false,
                "httpLoggingEnabled": false,
                "acrUseManagedIdentityCreds": false,
                "logsDirectorySizeLimit": 35,
                "detailedErrorLoggingEnabled": false,
                "publishingUsername": "REDACTED",
                "scmType": "GitHubAction",
                "use32BitWorkerProcess": false,
                "webSocketsEnabled": false,
                "alwaysOn": false,
                "managedPipelineMode": "Integrated",
                "virtualApplications": [
                    {
                        "virtualPath": "/",
                        "physicalPath": "site\\wwwroot",
                        "preloadEnabled": false
                    }
                ],
                "loadBalancing": "LeastRequests",
                "experiments": {
                    "rampUpRules": []
                },
                "autoHealEnabled": false,
                "vnetRouteAllEnabled": false,
                "vnetPrivatePortsCount": 0,
                "publicNetworkAccess": "Enabled",
                "cors": {
                    "allowedOrigins": [
                        "https://portal.azure.com"
                    ],
                    "supportCredentials": false
                },
                "localMySqlEnabled": false,
                "xManagedServiceIdentityId": 9033,
                "ipSecurityRestrictions": [
                    {
                        "ipAddress": "Any",
                        "action": "Allow",
                        "priority": 2147483647,
                        "name": "Allow all",
                        "description": "Allow all access"
                    }
                ],
                "scmIpSecurityRestrictions": [
                    {
                        "ipAddress": "Any",
                        "action": "Allow",
                        "priority": 2147483647,
                        "name": "Allow all",
                        "description": "Allow all access"
                    }
                ],
                "scmIpSecurityRestrictionsUseMain": false,
                "http20Enabled": false,
                "minTlsVersion": "1.2",
                "scmMinTlsVersion": "1.2",
                "ftpsState": "FtpsOnly",
                "preWarmedInstanceCount": 0,
                "functionAppScaleLimit": 100,
                "functionsRuntimeScaleMonitoringEnabled": false,
                "minimumElasticInstanceCount": 0,
                "azureStorageAccounts": {},
                "http20ProxyFlag": 0
            }
        },
        {
            "type": "Microsoft.Web/sites/deployments",
            "apiVersion": "2024-11-01",
            "name": "[concat(parameters('sites_grcal_dev_func_portal_name'), '/0cec8a29-9846-495d-aaab-66edf032fe31')]",
            "location": "Japan East",
            "dependsOn": [
                "[resourceId('Microsoft.Web/sites', parameters('sites_grcal_dev_func_portal_name'))]"
            ],
            "properties": {
                "status": 3,
                "deployer": "GITHUB_ZIP_DEPLOY_FUNCTIONS_V1",
                "start_time": "2025-09-18T07:42:33.0020633Z",
                "end_time": "2025-09-18T07:42:33.5822483Z",
                "active": false
            }
        },
        {
            "type": "Microsoft.Web/sites/hostNameBindings",
            "apiVersion": "2024-11-01",
            "name": "[concat(parameters('sites_grcal_dev_func_portal_name'), '/', parameters('sites_grcal_dev_func_portal_name'), '-a3f0h5eygzced4fk.japaneast-01.azurewebsites.net')]",
            "location": "Japan East",
            "dependsOn": [
                "[resourceId('Microsoft.Web/sites', parameters('sites_grcal_dev_func_portal_name'))]"
            ],
            "properties": {
                "siteName": "grcal-dev-func-portal",
                "hostNameType": "Verified"
            }
        }
    ]
}
```

Bicep

```bicep
param sites_grcal_dev_func_portal_name string = 'grcal-dev-func-portal'
param serverfarms_ASP_grcaldevfuncportalgroup_97b9_externalid string = '/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Web/serverfarms/ASP-grcaldevfuncportalgroup-97b9'
param userAssignedIdentities_grcal_dev_func_portal_uami_externalid string = '/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.ManagedIdentity/userAssignedIdentities/grcal-dev-func-portal-uami'

resource sites_grcal_dev_func_portal_name_resource 'Microsoft.Web/sites@2024-11-01' = {
  name: sites_grcal_dev_func_portal_name
  location: 'Japan East'
  tags: {
    'hidden-link: /app-insights-resource-id': '/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Insights/components/grcal-dev-func-portal'
  }
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourcegroups/grcal-dev-func-portal_group/providers/Microsoft.ManagedIdentity/userAssignedIdentities/grcal-dev-func-portal-uami': {}
    }
  }
  properties: {
    enabled: true
    hostNameSslStates: [
      {
        name: '${sites_grcal_dev_func_portal_name}-a3f0h5eygzced4fk.japaneast-01.azurewebsites.net'
        sslState: 'Disabled'
        hostType: 'Standard'
      }
      {
        name: '${sites_grcal_dev_func_portal_name}-a3f0h5eygzced4fk.scm.japaneast-01.azurewebsites.net'
        sslState: 'Disabled'
        hostType: 'Repository'
      }
    ]
    serverFarmId: serverfarms_ASP_grcaldevfuncportalgroup_97b9_externalid
    reserved: true
    isXenon: false
    hyperV: false
    dnsConfiguration: {}
    outboundVnetRouting: {
      allTraffic: false
      applicationTraffic: false
      contentShareTraffic: false
      imagePullTraffic: false
      backupRestoreTraffic: false
    }
    siteConfig: {
      numberOfWorkers: 1
      acrUseManagedIdentityCreds: false
      alwaysOn: false
      http20Enabled: false
      functionAppScaleLimit: 100
      minimumElasticInstanceCount: 0
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobcontainer'
          value: 'https://grcaldevfuncportalgbea3.blob.core.windows.net/app-package-${sites_grcal_dev_func_portal_name}-4cd7659'
          authentication: {
            type: 'userassignedidentity'
            userAssignedIdentityResourceId: userAssignedIdentities_grcal_dev_func_portal_uami_externalid
          }
        }
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
    }
    scmSiteAlsoStopped: false
    clientAffinityEnabled: false
    clientAffinityProxyEnabled: false
    clientCertEnabled: false
    clientCertMode: 'Required'
    hostNamesDisabled: false
    ipMode: 'IPv4'
    customDomainVerificationId: '0DA736A64E9249F296A477BF0D086832D4B90832AD3670A3AB0C1FCB2E14ACE4'
    containerSize: 1536
    dailyMemoryTimeQuota: 0
    httpsOnly: true
    endToEndEncryptionEnabled: false
    redundancyMode: 'None'
    publicNetworkAccess: 'Enabled'
    storageAccountRequired: false
    keyVaultReferenceIdentity: 'SystemAssigned'
    autoGeneratedDomainNameLabelScope: 'TenantReuse'
  }
}

resource sites_grcal_dev_func_portal_name_ftp 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: sites_grcal_dev_func_portal_name_resource
  name: 'ftp'
  location: 'Japan East'
  tags: {
    'hidden-link: /app-insights-resource-id': '/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Insights/components/grcal-dev-func-portal'
  }
  properties: {
    allow: false
  }
}

resource sites_grcal_dev_func_portal_name_scm 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: sites_grcal_dev_func_portal_name_resource
  name: 'scm'
  location: 'Japan East'
  tags: {
    'hidden-link: /app-insights-resource-id': '/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Insights/components/grcal-dev-func-portal'
  }
  properties: {
    allow: false
  }
}

resource sites_grcal_dev_func_portal_name_web 'Microsoft.Web/sites/config@2024-11-01' = {
  parent: sites_grcal_dev_func_portal_name_resource
  name: 'web'
  location: 'Japan East'
  tags: {
    'hidden-link: /app-insights-resource-id': '/subscriptions/68575d55-f60d-4d89-a32b-ad90af38faa6/resourceGroups/grcal-dev-func-portal_group/providers/Microsoft.Insights/components/grcal-dev-func-portal'
  }
  properties: {
    numberOfWorkers: 1
    defaultDocuments: [
      'Default.htm'
      'Default.html'
      'Default.asp'
      'index.htm'
      'index.html'
      'iisstart.htm'
      'default.aspx'
      'index.php'
    ]
    netFrameworkVersion: 'v4.0'
    requestTracingEnabled: false
    remoteDebuggingEnabled: false
    httpLoggingEnabled: false
    acrUseManagedIdentityCreds: false
    logsDirectorySizeLimit: 35
    detailedErrorLoggingEnabled: false
    publishingUsername: 'REDACTED'
    scmType: 'GitHubAction'
    use32BitWorkerProcess: false
    webSocketsEnabled: false
    alwaysOn: false
    managedPipelineMode: 'Integrated'
    virtualApplications: [
      {
        virtualPath: '/'
        physicalPath: 'site\\wwwroot'
        preloadEnabled: false
      }
    ]
    loadBalancing: 'LeastRequests'
    experiments: {
      rampUpRules: []
    }
    autoHealEnabled: false
    vnetRouteAllEnabled: false
    vnetPrivatePortsCount: 0
    publicNetworkAccess: 'Enabled'
    cors: {
      allowedOrigins: [
        'https://portal.azure.com'
      ]
      supportCredentials: false
    }
    localMySqlEnabled: false
    xManagedServiceIdentityId: 9033
    ipSecurityRestrictions: [
      {
        ipAddress: 'Any'
        action: 'Allow'
        priority: 2147483647
        name: 'Allow all'
        description: 'Allow all access'
      }
    ]
    scmIpSecurityRestrictions: [
      {
        ipAddress: 'Any'
        action: 'Allow'
        priority: 2147483647
        name: 'Allow all'
        description: 'Allow all access'
      }
    ]
    scmIpSecurityRestrictionsUseMain: false
    http20Enabled: false
    minTlsVersion: '1.2'
    scmMinTlsVersion: '1.2'
    ftpsState: 'FtpsOnly'
    preWarmedInstanceCount: 0
    functionAppScaleLimit: 100
    functionsRuntimeScaleMonitoringEnabled: false
    minimumElasticInstanceCount: 0
    azureStorageAccounts: {}
    http20ProxyFlag: 0
  }
}

resource sites_grcal_dev_func_portal_name_0cec8a29_9846_495d_aaab_66edf032fe31 'Microsoft.Web/sites/deployments@2024-11-01' = {
  parent: sites_grcal_dev_func_portal_name_resource
  name: '0cec8a29-9846-495d-aaab-66edf032fe31'
  location: 'Japan East'
  properties: {
    status: 3
    deployer: 'GITHUB_ZIP_DEPLOY_FUNCTIONS_V1'
    start_time: '2025-09-18T07:42:33.0020633Z'
    end_time: '2025-09-18T07:42:33.5822483Z'
    active: false
  }
}

resource sites_grcal_dev_func_portal_name_sites_grcal_dev_func_portal_name_a3f0h5eygzced4fk_japaneast_01_azurewebsites_net 'Microsoft.Web/sites/hostNameBindings@2024-11-01' = {
  parent: sites_grcal_dev_func_portal_name_resource
  name: '${sites_grcal_dev_func_portal_name}-a3f0h5eygzced4fk.japaneast-01.azurewebsites.net'
  location: 'Japan East'
  properties: {
    siteName: 'grcal-dev-func-portal'
    hostNameType: 'Verified'
  }
}
```