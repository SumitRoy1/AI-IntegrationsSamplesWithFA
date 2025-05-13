// ======================= //
// Private Endpoint Module //
// ======================= //

param peName string
param location string = resourceGroup().location
param subnetId string
param nicName string
param privateLinkServiceId string
param groupId string
param privateDNSId string

// ================ //
// PE Creation      //
// ================ //
resource peCreation 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: peName
  location: location
  properties: {
    subnet: {
      id: subnetId
    }
    customNetworkInterfaceName: nicName
    privateLinkServiceConnections: [
      {
        name: peName
        properties: {
          privateLinkServiceId: privateLinkServiceId
          groupIds: [
            groupId
          ]
          privateLinkServiceConnectionState: {
            status: 'Approved'
            description: 'Auto-Approved'
            actionsRequired: 'None'
          }
        }
      }
    ]
  }
}

// ========================== //
// PE DNS Zone Group Binding  //
// ========================== //
resource privateEndpointsDnsZoneGroups 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: peCreation
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: '${peCreation.name}-dns'
        properties: {
          privateDnsZoneId: privateDNSId
        }
      }
    ]
  }
}
