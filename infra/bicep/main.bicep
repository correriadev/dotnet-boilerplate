targetScope = 'resourceGroup'

@description('Environment identifier')
param environment string = 'dev'

@description('Workload identifier')
param workloadName string = 'pixlab'

@description('Primary location for resources')
param location string = resourceGroup().location

@description('Location for data resources (SQL and Redis) to prevent regional capacity constraints')
param dataLocation string = 'eastus2'

@description('SQL administrator login')
param sqlAdminLogin string = 'pixadmin'

@secure()
@description('Administrator password for Azure SQL Server')
param sqlAdminPassword string

// 1. Módulo de Segurança (Key Vault + User-Assigned Managed Identity)
module securityModule 'security.bicep' = {
  name: 'security-deployment'
  params: {
    location: location
    workloadName: workloadName
    environment: environment
  }
}

// 2. Módulo de Dados (Azure SQL Basic, Redis Basic, Service Bus Standard)
module dataModule 'data.bicep' = {
  name: 'data-deployment'
  params: {
    location: location
    dataLocation: dataLocation
    workloadName: workloadName
    environment: environment
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
    keyVaultName: securityModule.outputs.keyVaultName
  }
}

output keyVaultName string = securityModule.outputs.keyVaultName
output keyVaultUri string = securityModule.outputs.keyVaultUri
output managedIdentityId string = securityModule.outputs.managedIdentityId
output managedIdentityClientId string = securityModule.outputs.managedIdentityClientId
output sqlServerFqdn string = dataModule.outputs.sqlServerFqdn
output sqlDatabaseName string = dataModule.outputs.sqlDatabaseName
output redisHostName string = dataModule.outputs.redisHostName
output serviceBusFqdn string = dataModule.outputs.serviceBusNamespaceFqdn
output serviceBusQueue string = dataModule.outputs.serviceBusQueueName

