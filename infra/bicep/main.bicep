targetScope = 'resourceGroup'

@description('Environment name')
@allowed([
  'dev'
  'hml'
  'prod'
])
param environment string = 'dev'

@description('Primary Azure location')
param location string = resourceGroup().location

@description('Workload name identifier')
param workloadName string = 'pix'

@secure()
@description('Administrator password for Azure SQL Server')
param sqlAdminPassword string

// 1. Network Module (VNet, subnets, NSGs, Private DNS Zones)
module networkModule 'network.bicep' = {
  name: 'network-deployment'
  params: {
    location: location
    workloadName: workloadName
    environment: environment
  }
}

// 2. Security Module (Key Vault, Managed Identity, Private Endpoint)
module securityModule 'security.bicep' = {
  name: 'security-deployment'
  params: {
    location: location
    workloadName: workloadName
    environment: environment
    snetDataId: networkModule.outputs.snetDataId
    dnsZoneKeyVaultId: networkModule.outputs.dnsZoneKeyVaultId
  }
}

// 3. Data Module (Azure SQL ReadScale, Redis, Service Bus, Private Endpoints)
module dataModule 'data.bicep' = {
  name: 'data-deployment'
  params: {
    location: location
    workloadName: workloadName
    environment: environment
    snetDataId: networkModule.outputs.snetDataId
    dnsZoneSqlId: networkModule.outputs.dnsZoneSqlId
    dnsZoneRedisId: networkModule.outputs.dnsZoneRedisId
    dnsZoneServiceBusId: networkModule.outputs.dnsZoneServiceBusId
    adminPassword: sqlAdminPassword
  }
}

// 4. Apps Module (Container Apps Environment integrado à snet-apps)
module appsModule 'apps.bicep' = {
  name: 'apps-deployment'
  params: {
    location: location
    workloadName: workloadName
    environment: environment
    snetAppsId: networkModule.outputs.snetAppsId
    managedIdentityId: securityModule.outputs.managedIdentityId
  }
}

output vnetId string = networkModule.outputs.vnetId
output keyVaultUri string = securityModule.outputs.keyVaultUri
output sqlServerFqdn string = dataModule.outputs.sqlServerFqdn
output containerAppsEnvId string = appsModule.outputs.containerAppsEnvId
