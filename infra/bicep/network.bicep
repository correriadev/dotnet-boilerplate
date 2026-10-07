@description('Location for all network resources')
param location string = resourceGroup().location

@description('Workload name prefix')
param workloadName string = 'pix'

@description('Environment name')
param environment string = 'dev'

var vnetName = 'vnet-${workloadName}-${environment}'
var vnetAddressPrefix = '10.0.0.0/16'

var snetGatewayPrefix = '10.0.1.0/24'
var snetAppsPrefix = '10.0.2.0/24'
var snetDataPrefix = '10.0.3.0/24'

// 1. NSG Gateway
resource nsgGateway 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: 'nsg-snet-gateway-${environment}'
  location: location
  properties: {
    securityRules: [
      {
        name: 'Allow-Http-Https-Inbound'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRanges: [
            '80'
            '443'
            '8080'
          ]
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: snetGatewayPrefix
        }
      }
    ]
  }
}

// 2. NSG Apps
resource nsgApps 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: 'nsg-snet-apps-${environment}'
  location: location
  properties: {
    securityRules: [
      {
        name: 'Allow-Gateway-To-Apps'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '8080'
          sourceAddressPrefix: snetGatewayPrefix
          destinationAddressPrefix: snetAppsPrefix
        }
      }
      {
        name: 'Deny-All-Other-Inbound-Apps'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: snetAppsPrefix
        }
      }
      {
        name: 'Deny-Internet-Inbound-Apps'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: snetAppsPrefix
        }
      }
    ]
  }
}

// 3. NSG Data (Strict isolation - TS-INFRA-13: Deny Internet Inbound 100%, tráfego apenas da snet-apps)
resource nsgData 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: 'nsg-snet-data-${environment}'
  location: location
  properties: {
    securityRules: [
      {
        name: 'Allow-Apps-To-Sql'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '1433'
          sourceAddressPrefix: snetAppsPrefix
          destinationAddressPrefix: snetDataPrefix
        }
      }
      {
        name: 'Allow-Apps-To-Redis'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '6379'
          sourceAddressPrefix: snetAppsPrefix
          destinationAddressPrefix: snetDataPrefix
        }
      }
      {
        name: 'Allow-Apps-To-ServiceBus'
        properties: {
          priority: 120
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRanges: [
            '5671'
            '5672'
            '443'
          ]
          sourceAddressPrefix: snetAppsPrefix
          destinationAddressPrefix: snetDataPrefix
        }
      }
      {
        name: 'Allow-Apps-To-KeyVault'
        properties: {
          priority: 130
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: snetAppsPrefix
          destinationAddressPrefix: snetDataPrefix
        }
      }
      {
        name: 'Deny-Gateway-And-Other-Vnet-Inbound'
        properties: {
          priority: 4000
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: snetDataPrefix
        }
      }
      {
        name: 'Deny-Internet-Inbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'Internet'
          destinationAddressPrefix: snetDataPrefix
        }
      }
    ]
  }
}

// Virtual Network
resource vnet 'Microsoft.Network/virtualNetworks@2023-09-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: 'snet-gateway'
        properties: {
          addressPrefix: snetGatewayPrefix
          networkSecurityGroup: {
            id: nsgGateway.id
          }
        }
      }
      {
        name: 'snet-apps'
        properties: {
          addressPrefix: snetAppsPrefix
          networkSecurityGroup: {
            id: nsgApps.id
          }
          delegations: [
            {
              name: 'containerAppsDelegation'
              properties: {
                serviceName: 'Microsoft.App/environments'
              }
            }
          ]
        }
      }
      {
        name: 'snet-data'
        properties: {
          addressPrefix: snetDataPrefix
          networkSecurityGroup: {
            id: nsgData.id
          }
          privateEndpointNetworkPolicies: 'Enabled'
        }
      }
    ]
  }
}

// Private DNS Zones
var dnsZones = [
  'privatelink.database.windows.net'
  'privatelink.redis.cache.windows.net'
  'privatelink.servicebus.windows.net'
  'privatelink.vaultcore.azure.net'
]

resource privateDnsZones 'Microsoft.Network/privateDnsZones@2020-06-01' = [for zone in dnsZones: {
  name: zone
  location: 'global'
}]

resource privateDnsZoneVnetLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = [for (zone, i) in dnsZones: {
  parent: privateDnsZones[i]
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}]

output vnetId string = vnet.id
output vnetName string = vnet.name
output snetGatewayId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'snet-gateway')
output snetAppsId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'snet-apps')
output snetDataId string = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'snet-data')
output dnsZoneSqlId string = resourceId('Microsoft.Network/privateDnsZones', 'privatelink.database.windows.net')
output dnsZoneRedisId string = resourceId('Microsoft.Network/privateDnsZones', 'privatelink.redis.cache.windows.net')
output dnsZoneServiceBusId string = resourceId('Microsoft.Network/privateDnsZones', 'privatelink.servicebus.windows.net')
output dnsZoneKeyVaultId string = resourceId('Microsoft.Network/privateDnsZones', 'privatelink.vaultcore.azure.net')
