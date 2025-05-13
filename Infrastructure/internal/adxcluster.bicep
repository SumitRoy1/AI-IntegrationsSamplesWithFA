// ============================ //
// Sanitary ADX Cluster Bicep  //
// ============================ //

param name string
param location string = resourceGroup().location
param tags object = {}
param sku object = {} // Expected: { name: '', tier: '', capacity: 0 }
param databases array = []
param subnetId string
param networkingRG string
param softDeletePeriod int = 365
param hotCachePeriod int = 31
param faname string
param gdprScanToolIP string // Specific IP allowed for GDPR scanner

// ===================== //
// ADX Cluster Creation  //
// ===================== //
resource azureDataExplorer 'Microsoft.Kusto/clusters@2023-08-15' = {
  name: name
  location: location
  sku: {
    name: sku.name
    tier: sku.tier
    capacity: sku.capacity
  }
  tags: union(tags, {
    'Environment': 'Production'
  })
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    enableDiskEncryption: false
    enableStreamingIngest: false
    publicNetworkAccess: 'Enabled'
    trustedExternalTenants: []
    enablePurge: false
    enableDoubleEncryption: false
    enableAutoStop: false
    allowedIpRangeList: [
      gdprScanToolIP
    ]
  }
}

output id string = azureDataExplorer.id
output name string = azureDataExplorer.name

// ======================== //
// Private Endpoint Config  //
// ======================== //
resource adxPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: '${name}-pe'
  location: location
  properties: {
    subnet: {
      id: subnetId
    }
    customNetworkInterfaceName: '${name}-pe-nic'
    privateLinkServiceConnections: [
      {
        name: name
        properties: {
          privateLinkServiceId: azureDataExplorer.id
          groupIds: [
            'cluster'
          ]
          privateLinkServiceConnectionState: {
            status: 'Approved'
            actionsRequired: 'None'
          }
        }
      }
    ]
  }
}

// ========================== //
// Private DNS Zone Bindings  //
// ========================== //
resource privateDnsZones_blob 'Microsoft.Network/privateDnsZones@2020-06-01' existing = {
  name: 'privatelink.blob.<storage-suffix>'
  scope: resourceGroup(networkingRG)
}

resource privateDnsZones_queue 'Microsoft.Network/privateDnsZones@2020-06-01' existing = {
  name: 'privatelink.queue.<storage-suffix>'
  scope: resourceGroup(networkingRG)
}

resource privateDnsZones_table 'Microsoft.Network/privateDnsZones@2020-06-01' existing = {
  name: 'privatelink.table.<storage-suffix>'
  scope: resourceGroup(networkingRG)
}

resource privateDnsZones_kusto 'Microsoft.Network/privateDnsZones@2020-06-01' existing = {
  name: 'privatelink.${location}.kusto.windows.net'
  scope: resourceGroup(networkingRG)
}

resource privateEndpointsDnsZoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: adxPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-kusto'
        properties: {
          privateDnsZoneId: privateDnsZones_kusto.id
        }
      }
      {
        name: 'privatelink-blob'
        properties: {
          privateDnsZoneId: privateDnsZones_blob.id
        }
      }
      {
        name: 'privatelink-queue'
        properties: {
          privateDnsZoneId: privateDnsZones_queue.id
        }
      }
      {
        name: 'privatelink-table'
        properties: {
          privateDnsZoneId: privateDnsZones_table.id
        }
      }
    ]
  }
}

// ======================== //
// ADX Databases Provision  //
// ======================== //
resource azureDataExplorerDatabases 'Microsoft.Kusto/clusters/databases@2023-08-15' = [
  for db in databases: {
    name: db.name
    location: location
    kind: 'ReadWrite'
    parent: azureDataExplorer
    properties: {
      softDeletePeriod: 'P${softDeletePeriod}D'
      hotCachePeriod: 'P${hotCachePeriod}D'
    }
  }
]

// =============================== //
// Assign MI Role to ADX Cluster   //
// =============================== //
resource functionApp 'Microsoft.Web/sites@2023-12-01' existing = {
  name: faname
}

resource roleAssignment 'Microsoft.Kusto/clusters/principalAssignments@2022-12-29' = {
  name: guid(azureDataExplorerDatabases[0].id, 'AllDatabasesAdmin')
  parent: azureDataExplorer
  properties: {
    role: 'AllDatabasesAdmin'
    principalId: functionApp.identity.principalId
    principalType: 'App'
    tenantId: subscription().tenantId
  }
}
