// Infraestructura de Deep Arquitectura en Azure.
// Despliegue (2026-11-18: rg-deeparq en westus3; en esta suscripción centralus/eastus/eastus2/brazilsouth no tienen
// cupo de App Service B1 y westus2/eastus2 no aceptan servidores SQL nuevos):
//   az group create -n rg-deeparq -l westus3   (App Service y SQL en la misma región: ver usarVnet)
//   az deployment group create -g rg-deeparquitectura -f infra/main.bicep -p infra/main.parameters.json \
//      -p sqlAdminPassword=<...> adminPasswordHash=<...> [azureOpenAIApiKey=<...>]

@description('Prefijo para nombres de recursos (minúsculas, sin espacios).')
param nombre string = 'deeparquitectura'

param location string = resourceGroup().location

@description('Región del App Service. Separada porque algunas suscripciones no tienen cuota de App Service en todas las regiones.')
param appServiceLocation string = location

@description('SKU del App Service Plan. B1 (~USD 13/mes) mantiene la app siempre encendida; F1 es gratis pero se duerme y tiene límite de CPU diario.')
@allowed(['F1', 'B1', 'B2', 'S1', 'P0v3'])
param appServiceSku string = 'B1'

@description('Usuario administrador del servidor Azure SQL.')
param sqlAdminLogin string = 'deeparqadmin'

@secure()
@description('Contraseña del administrador de Azure SQL.')
param sqlAdminPassword string

@description('Usuario del panel /admin.')
param adminUsuario string = 'gaston'

@secure()
@description('Hash de la contraseña del panel: dotnet run --project src/DeepArquitectura.Web -- hash-password "<clave>"')
param adminPasswordHash string

@description('Endpoint de Azure OpenAI (vacío = agente CM deshabilitado). Puede ser el mismo recurso que ya usás en otros proyectos.')
param azureOpenAIEndpoint string = ''

@secure()
param azureOpenAIApiKey string = ''

param azureOpenAIDeployment string = 'gpt-4o-mini'

var sufijo = uniqueString(resourceGroup().id)
var esFree = appServiceSku == 'F1'

// La base solo acepta conexiones desde la subred de la app (VNet integration + service endpoint, sin costo).
// Requiere SKU B1 o superior y que App Service y SQL estén en la misma región; si no, se cae a la regla
// "AllowAzureServices", que deja pasar a CUALQUIER servicio de Azure (también de otros clientes).
var usarVnet = !esFree && location == appServiceLocation
var nombreVnet = 'vnet-${nombre}'
var subredApp = resourceId('Microsoft.Network/virtualNetworks/subnets', nombreVnet, 'app')

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = if (usarVnet) {
  name: nombreVnet
  location: appServiceLocation
  properties: {
    addressSpace: { addressPrefixes: ['10.20.0.0/24'] }
    subnets: [
      {
        name: 'app'
        properties: {
          addressPrefix: '10.20.0.0/26'
          delegations: [{ name: 'app-service', properties: { serviceName: 'Microsoft.Web/serverFarms' } }]
          serviceEndpoints: [{ service: 'Microsoft.Sql' }]
        }
      }
    ]
  }
}

// ---------- Base de datos: Azure SQL serverless con la oferta gratuita ----------
resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${nombre}-${sufijo}'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Solo la subred de la app llega a la base (ver usarVnet).
resource sqlReglaVnet 'Microsoft.Sql/servers/virtualNetworkRules@2023-08-01-preview' = if (usarVnet) {
  parent: sql
  name: 'app-service'
  properties: {
    virtualNetworkSubnetId: subredApp
    ignoreMissingVnetServiceEndpoint: false
  }
  dependsOn: [vnet]
}

// Plan B (F1 o regiones distintas): cualquier servicio de Azure. No abre a Internet, pero sí a otros clientes de Azure.
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!usarVnet) {
  parent: sql
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// La app entra a SQL con su identidad administrada (sin contraseña en App Settings). La contraseña del
// administrador SQL queda solo para emergencias: la app ya no la conoce.
// Nota: la identidad queda como administradora Entra del servidor (lo necesita para las migraciones al
// arrancar); el servidor tiene una sola base, así que en la práctica equivale a db_owner de esa base.
resource sqlAdminEntra 'Microsoft.Sql/servers/administrators@2023-08-01-preview' = {
  parent: sql
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: web.name
    sid: web.identity.principalId
    tenantId: subscription().tenantId
  }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'sqldb-${nombre}'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    // Oferta gratuita: 100.000 vCore-segundos + 32 GB por mes; si se agota, la base se pausa (no cobra).
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

// ---------- Fotos y videos de obra (app del capataz) ----------
// Contenedor privado: los archivos solo se ven a través del panel (con login), nunca por URL pública.
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: toLower('st${take(nombre, 10)}${take(sufijo, 8)}')
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    // Sin claves: la app entra con su identidad administrada (rol Storage Blob Data Contributor, más abajo).
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource contenedorMedios 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'medios-obra'
  properties: { publicAccess: 'None' }
}

// ---------- Monitoreo: Application Insights (gratis hasta 5 GB/mes de datos) ----------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${nombre}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.5') } // tope diario: un error en bucle no dispara la factura
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${nombre}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------- Claves: Key Vault (la app las lee con su identidad; no quedan a la vista en la configuración) ----------
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${take(nombre, 10)}-${take(sufijo, 8)}'
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource secretoAdminHash 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: 'admin-password-hash'
  properties: { value: adminPasswordHash }
}

resource secretoOpenAI 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(azureOpenAIApiKey)) {
  parent: vault
  name: 'azure-openai-key'
  properties: { value: azureOpenAIApiKey }
}

// ---------- App Service (Linux, .NET 10) ----------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${nombre}'
  location: appServiceLocation
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true
  }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${nombre}-${sufijo}'
  location: appServiceLocation
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    virtualNetworkSubnetId: usarVnet ? subredApp : null
    httpsOnly: true
    clientAffinityEnabled: true // Blazor Server: el circuito vive en una instancia
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: !esFree
      webSocketsEnabled: true // SignalR para el panel interactivo
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'Admin__Usuario', value: adminUsuario }
        { name: 'Admin__PasswordHash', value: '@Microsoft.KeyVault(SecretUri=${secretoAdminHash.properties.secretUri})' }
        { name: 'AzureOpenAI__Endpoint', value: azureOpenAIEndpoint }
        { name: 'AzureOpenAI__ApiKey', value: empty(azureOpenAIApiKey) ? '' : '@Microsoft.KeyVault(SecretUri=${secretoOpenAI!.properties.secretUri})' }
        { name: 'AzureOpenAI__Deployment', value: azureOpenAIDeployment }
        { name: 'Database__MigrarAlIniciar', value: 'true' }
        { name: 'Medios__Contenedor', value: contenedorMedios.name }
        { name: 'Medios__BlobUrl', value: storage.properties.primaryEndpoints.blob }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
      ]
      connectionStrings: [
        {
          name: 'DeepArquitectura'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${db.name};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
        }
      ]
    }
  }
  dependsOn: [vnet]
}

// ---------- Permisos de la identidad de la app (sin claves ni contraseñas) ----------
var rolBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var rolKeyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'

resource appEnBlob 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, web.id, rolBlobDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', rolBlobDataContributor)
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource appEnVault 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, web.id, rolKeyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', rolKeyVaultSecretsUser)
    principalId: web.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = web.name
output webAppUrl string = 'https://${web.properties.defaultHostName}'
output sqlServer string = sql.properties.fullyQualifiedDomainName
output sqlSoloDesdeLaApp bool = usarVnet
