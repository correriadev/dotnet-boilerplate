@description('Location for security resources')
param location string = resourceGroup().location

@description('Workload name identifier')
param workloadName string = 'pixlab'

@description('Environment name')
param environment string = 'dev'

// Key Vault name: max 24 chars, alphanumeric and hyphens
var keyVaultName = take('kv-${workloadName}-${environment}-${uniqueString(resourceGroup().id)}', 24)
var identityName = 'uai-${workloadName}-${environment}'

// 1. User Assigned Managed Identity
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

// 2. Azure Key Vault (Standard)
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// 3. Role Assignment: Key Vault Secrets User (4633458b-17de-408a-b874-0445c86b69e6) para a Managed Identity
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, managedIdentity.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Role Assignment: Key Vault Administrator (00482a5a-887f-4fb3-b363-3b7fe8e74483) para o criador do deployment
var keyVaultAdminRoleId = '00482a5a-887f-4fb3-b363-3b7fe8e74483'
param deployerPrincipalId string = ''

resource deployerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerPrincipalId)) {
  name: guid(keyVault.id, deployerPrincipalId, keyVaultAdminRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultAdminRoleId)
    principalId: deployerPrincipalId
    principalType: 'User'
  }
}

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output managedIdentityId string = managedIdentity.id
output managedIdentityClientId string = managedIdentity.properties.clientId
output managedIdentityPrincipalId string = managedIdentity.properties.principalId

