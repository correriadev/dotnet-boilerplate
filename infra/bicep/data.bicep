@description('Location for general resources')
param location string = resourceGroup().location

@description('Location for data resources (SQL and Redis) to prevent regional capacity constraints')
param dataLocation string = 'eastus2'

@description('Workload name identifier')
param workloadName string = 'pixlab'

@description('Environment name')
param environment string = 'dev'

@description('Administrator login for Azure SQL Server')
param adminLogin string = 'pixadmin'

@secure()
@description('Administrator password for Azure SQL Server')
param adminPassword string

@description('Target Key Vault name to store connection strings')
param keyVaultName string

var suffix = uniqueString(resourceGroup().id)
var sqlServerName = take('sql-${workloadName}-${environment}-${suffix}', 63)
var sqlDbName = 'sqldb-${workloadName}-${environment}'
var redisName = take('redis-${workloadName}-${environment}-${suffix}', 63)
var serviceBusName = take('sb-${workloadName}-${environment}-${suffix}', 50)

// 1. Azure SQL Server & Database (Econômico para Estudo: SKU Basic em região com capacidade)
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: dataLocation
  properties: {
    administratorLogin: adminLogin
    administratorLoginPassword: adminPassword
    version: '12.0'
    publicNetworkAccess: 'Enabled'
    minimalTlsVersion: '1.2'
  }
}

// Permite conexões de recursos dentro do Azure (Container Apps, etc.)
resource sqlFirewallRuleAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: dataLocation
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648 // 2GB
  }
}

// 2. Azure Cache for Redis (Econômico: SKU Basic C0 em região com capacidade)
resource redisCache 'Microsoft.Cache/Redis@2023-08-01' = {
  name: redisName
  location: dataLocation
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// 3. Azure Service Bus Namespace (Standard - Suporta Sessions FIFO e Queues)
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusName
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
    capacity: 1
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource serviceBusQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'pix-commands'
  properties: {
    requiresSession: true
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    maxDeliveryCount: 10
  }
}

resource sbAuthRule 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2022-10-01-preview' existing = {
  parent: serviceBusNamespace
  name: 'RootManageSharedAccessKey'
}

// 4. Guarda Segredos no Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource secretSql 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'SqlConnectionString'
  properties: {
    value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabase.name};Persist Security Info=False;User ID=${adminLogin};Password=${adminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
  }
}

resource secretRedis 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'RedisConnectionString'
  properties: {
    value: '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${redisCache.listKeys().primaryKey},ssl=True,abortConnect=False'
  }
}

resource secretServiceBus 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ServiceBusConnectionString'
  properties: {
    value: sbAuthRule.listKeys().primaryConnectionString
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output redisHostName string = redisCache.properties.hostName
output serviceBusNamespaceFqdn string = '${serviceBusNamespace.name}.servicebus.windows.net'
output serviceBusQueueName string = serviceBusQueue.name

