@description('Location for data resources')
param location string = resourceGroup().location

@description('Workload name')
param workloadName string = 'pix'

@description('Environment name')
param environment string = 'dev'

@description('Data Subnet Resource ID')
param snetDataId string

@description('SQL Private DNS Zone Resource ID')
param dnsZoneSqlId string

@description('Redis Private DNS Zone Resource ID')
param dnsZoneRedisId string

@description('Service Bus Private DNS Zone Resource ID')
param dnsZoneServiceBusId string

@description('Administrator login for SQL Server')
param adminLogin string = 'pixadmin'

@secure()
@description('Administrator password for SQL Server')
param adminPassword string

var sqlServerName = 'sql-${workloadName}-${environment}'
var sqlDbName = 'sqldb-${workloadName}-${environment}'
var redisName = 'redis-${workloadName}-${environment}'
var serviceBusName = 'sb-${workloadName}-${environment}'

// 1. Azure SQL Server & Database com réplica síncrona (readScale: 'Enabled')
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: adminLogin
    administratorLoginPassword: adminPassword
    version: '12.0'
    publicNetworkAccess: 'Disabled'
    minimalTlsVersion: '1.2'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDbName
  location: location
  sku: {
    name: 'BC_Gen5_2'
    tier: 'BusinessCritical'
    capacity: 2
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    readScale: 'Enabled'
    zoneRedundant: true
    maxSizeBytes: 268435456000
  }
}

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${sqlServerName}'
  location: location
  properties: {
    subnet: {
      id: snetDataId
    }
    privateLinkServiceConnections: [
      {
        name: 'plsc-${sqlServerName}'
        properties: {
          privateLinkServiceId: sqlServer.id
          groupIds: [
            'sqlServer'
          ]
        }
      }
    ]
  }
}

resource sqlDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: sqlPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-database-windows-net'
        properties: {
          privateDnsZoneId: dnsZoneSqlId
        }
      }
    ]
  }
}

// 2. Azure Cache for Redis
resource redisCache 'Microsoft.Cache/Redis@2023-08-01' = {
  name: redisName
  location: location
  properties: {
    sku: {
      name: 'Premium'
      family: 'P'
      capacity: 1
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
  }
}

resource redisPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${redisName}'
  location: location
  properties: {
    subnet: {
      id: snetDataId
    }
    privateLinkServiceConnections: [
      {
        name: 'plsc-${redisName}'
        properties: {
          privateLinkServiceId: redisCache.id
          groupIds: [
            'redisCache'
          ]
        }
      }
    ]
  }
}

resource redisDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: redisPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-redis-cache-windows-net'
        properties: {
          privateDnsZoneId: dnsZoneRedisId
        }
      }
    ]
  }
}

// 3. Azure Service Bus (Premium SKU para Private Endpoint)
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusName
  location: location
  sku: {
    name: 'Premium'
    tier: 'Premium'
    capacity: 1
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
  }
}

resource serviceBusPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${serviceBusName}'
  location: location
  properties: {
    subnet: {
      id: snetDataId
    }
    privateLinkServiceConnections: [
      {
        name: 'plsc-${serviceBusName}'
        properties: {
          privateLinkServiceId: serviceBusNamespace.id
          groupIds: [
            'namespace'
          ]
        }
      }
    ]
  }
}

resource serviceBusDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: serviceBusPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-servicebus-windows-net'
        properties: {
          privateDnsZoneId: dnsZoneServiceBusId
        }
      }
    ]
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output redisHostName string = redisCache.properties.hostName
output serviceBusNamespaceFqdn string = '${serviceBusNamespace.name}.servicebus.windows.net'
