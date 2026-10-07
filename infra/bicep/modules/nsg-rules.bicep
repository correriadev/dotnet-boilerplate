@description('Location for all resources')
param location string

@description('Name of the Network Security Group')
param nsgName string

@description('Security rules configuration')
param securityRules array

resource nsg 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: nsgName
  location: location
  properties: {
    securityRules: securityRules
  }
}

output nsgId string = nsg.id
output nsgName string = nsg.name
