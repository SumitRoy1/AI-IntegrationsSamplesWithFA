// 1. Create Storage Account with network isolation, create Private Endpoint
// 2. Create Function App with managed identity and network isolation (VNet integration + inbound PE)
// 3. Assign 'Storage Blob Data Owner' role on Storage Account to the Function App's managed identity
// 4. Connect Function App to the Storage Account using environment variables (not shown here)

param location string = resourceGroup().location
param subnetId string
param faname string
param faOutboundSubnetId string
param networkingRG string

// Storage Account with private networking
resource faStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: toLower('strg${faname}')
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  tags: {}
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    networkAcls: {
      bypass: 'AzureServices'
      virtualNetworkRules: []
      ipRules: []
      defaultAction: 'Allow'
    }
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Disabled'
  }
}

// Private Endpoint for Blob
module storageBlobPE './pe.bicep' = {
  name: 'pe-blob-${uniqueString(deployment().name, location)}'
  params: {
    peName: '${faStorageAccount.name}-blob-pe'
    subnetId: subnetId
    nicName: '${faStorageAccount.name}-pe-blob-nic'
    privateLinkServiceId: faStorageAccount.id
    groupId: 'blob'
    privateDNSId: privateDnsZones_blob.id
  }
}

// App Service Plan for Function App
resource appServicePlan 'Microsoft.Web/serverfarms@2022-03-01' = {
  name: '${faname}ASP'
  location: location
  sku: {
    tier: 'Standard'
    name: 'S1'
    size: 'S1'
    family: 'S'
    capacity: 1
  }
  kind: 'app'
}

// Function App with VNet integration and managed identity
resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: faname
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      alwaysOn: true
      publicNetworkAccess: 'Enabled'
    }
    virtualNetworkSubnetId: faOutboundSubnetId
    vnetRouteAllEnabled: true
  }
}

// Private Endpoint for inbound access to Function App
module faInboundPE './pe.bicep' = {
  name: 'pe-fainbound-${uniqueString(deployment().name, location)}'
  params: {
    peName: '${faname}-fainbound-pe'
    subnetId: subnetId
    nicName: '${faname}-pe-fainbound-nic'
    privateLinkServiceId: functionApp.id
    groupId: 'sites'
    privateDNSId: privateDnsZones_website.id
  }
}

// Assign Storage Blob Data Owner role to Function App's MI
resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionApp.id)
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' // Storage Blob Data Owner
    )
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
  scope: faStorageAccount
}

// Reference existing Private DNS Zones
resource privateDnsZones_blob 'Microsoft.Network/privateDnsZones@2020-06-01' existing = {
  name: 'privatelink.blob.<your-storage-suffix>' // Replace with actual suffix if needed
  scope: resourceGroup(networkingRG)
}

resource privateDnsZones_website 'Microsoft.Network/privateDnsZones@2020-06-01' existing = {
  name: 'privatelink.azurewebsites.net'
  scope: resourceGroup(networkingRG)
}

// IP restrictions for Function App (allow AzureCloud in specific region)
resource webAppAccessRestrictions 'Microsoft.Web/sites/config@2022-09-01' = {
  parent: functionApp
  name: 'web'
  properties: {
    scmIpSecurityRestrictionsUseMain: false
    ipSecurityRestrictions: [
      {
        name: 'AllowAzureCloud${location}'
        priority: 101
        action: 'Allow'
        tag: 'ServiceTag'
        ipAddress: 'AzureCloud.${location}'
      }
    ]
    scmIpSecurityRestrictions: []
  }
}

output name string = functionApp.name
