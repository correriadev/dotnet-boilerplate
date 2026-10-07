@description('Location for application resources')
param location string = resourceGroup().location

@description('Workload name')
param workloadName string = 'pix'

@description('Environment name')
param environment string = 'dev'

@description('Apps Subnet Resource ID for Container Apps Environment')
param snetAppsId string

@description('User Assigned Managed Identity Resource ID')
param managedIdentityId string

var logAnalyticsName = 'log-${workloadName}-${environment}'
var caeName = 'cae-${workloadName}-${environment}'

// Log Analytics Workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Container Apps Managed Environment integrado à snet-apps
resource containerAppsEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: caeName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: snetAppsId
      internal: true
    }
    zoneRedundant: true
  }
}

output containerAppsEnvId string = containerAppsEnv.id
output containerAppsEnvDefaultDomain string = containerAppsEnv.properties.defaultDomain
