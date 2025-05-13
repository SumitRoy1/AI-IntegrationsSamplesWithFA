// ============================ //
//   Sanitary OpenAI Bicep     //
// ============================ //

param accounts_aibuildwatcher_we_name string = 'openai-account-name'
param subnetId string
param location string = 'westeurope'
param networkingRG string
param faname string

// ===================== //
// Azure OpenAI Resource //
// ===================== //
resource accounts_openai 'Microsoft.CognitiveServices/accounts@2024-04-01-preview' = {
  name: accounts_aibuildwatcher_we_name
  location: location
  sku: {
    name: 'S0'
  }
  kind: 'OpenAI'
  properties: {
    customSubDomainName: accounts_aibuildwatcher_we_name
    networkAcls: {
      defaultAction: 'Deny'
      virtualNetworkRules: [
        {
          id: subnetId
        }
      ]
      ipRules: []
    }
    publicNetworkAccess: 'Disabled'
    disableLocalAuth: true
  }
}

// ========================== //
// GPT-35 Turbo Deployment    //
// ========================== //
resource gpt35turbo_deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-04-01-preview' = {
  parent: accounts_openai
  name: 'gpt35turbo'
  sku: {
    name: 'Standard'
    capacity: 120
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-35-turbo'
      version: '0301'
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    currentCapacity: 120
    raiPolicyName: 'Microsoft.Default'
  }
}

// ========================== //
// Private Endpoint Module    //
// ========================== //
module openAiPE './pe.bicep' = {
  name: 'pe-openai-${uniqueString(deployment().name, location)}'
  params: {
    peName: '${accounts_aibuildwatcher_we_name}-pe'
    subnetId: subnetId
    nicName: '${accounts_aibuildwatcher_we_name}-nic'
    privateLinkServiceId: accounts_openai.id
    groupId: 'account'
    privateDNSId: privateDnsZones_website.id
  }
}

// ========================= //
// Private DNS Zone (OpenAI) //
// ========================= //
resource privateDnsZones_website 'Microsoft.Network/privateDnsZones@2020-06-01' existing = {
  name: 'privatelink.openai.azure.com'
  scope: resourceGroup(networkingRG)
}

// ================================ //
// Function App Reference (MI Auth) //
// ================================ //
resource functionApp 'Microsoft.Web/sites@2023-12-01' existing = {
  name: faname
}

// ============================================ //
// Role Assignment: OpenAI Contributor to FA MI //
// ============================================ //
resource roleAssignmentOpenAI 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionApp.id, 'openAI')
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'a001fd3d-188f-4b5d-821b-7da978bf7442' // Cognitive Services OpenAI Contributor
    )
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
  scope: accounts_openai
}

// ================== //
// Outputs            //
// ================== //
output endpoint string = accounts_openai.properties.endpoint
output id string = accounts_openai.id
output name string = accounts_openai.name
