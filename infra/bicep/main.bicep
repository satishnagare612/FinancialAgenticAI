// FinancialAI — Azure infrastructure
// Provisions: ACR, Cosmos DB (SQL API, serverless), Service Bus (Standard),
// Key Vault, Log Analytics, App Insights, Container Apps env, two Container
// Apps (api + ai), one user-assigned managed identity with KV + ACR access.

targetScope = 'resourceGroup'

@description('Environment name')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'staging'

@description('Azure region')
param location string = resourceGroup().location

@description('Base name prefix (lowercase alphanumeric + hyphens)')
param baseName string = 'financialai'

@description('Auth signing key (32+ bytes). Pass via --parameters or from Key Vault.')
@secure()
param authSigningKey string

@description('Container image tags')
param apiImageTag string = 'latest'
param aiImageTag string = 'latest'

// ---------- Log Analytics + App Insights ----------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${baseName}-log-${environment}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${baseName}-ai-${environment}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------- Container Registry ----------
var acrName = toLower(take('${baseName}acr${uniqueString(resourceGroup().id)}', 50))
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

// ---------- Cosmos DB ----------
var cosmosName = toLower(take('${baseName}-cosmos-${uniqueString(resourceGroup().id)}', 44))
resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' = {
  name: cosmosName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    locations: [ { locationName: location, failoverPriority: 0 } ]
    capabilities: [ { name: 'EnableServerless' } ]
    disableKeyBasedMetadataWriteAccess: false
    enableFreeTier: false
  }
}

resource cosmosDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-11-15' = {
  parent: cosmos
  name: 'financialai'
  properties: { resource: { id: 'financialai' } }
}

var cosmosContainers = [
  { name: 'profiles',           partitionKey: '/userId',   ttl: -1    }
  { name: 'reports',            partitionKey: '/reportId', ttl: -1    }
  { name: 'sagas',              partitionKey: '/id',       ttl: -1    }
  { name: 'processed-messages', partitionKey: '/id',       ttl: 86400 }
  { name: 'human-reviews',      partitionKey: '/reportId', ttl: -1    }
]

resource cosmosContainersRes 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = [
  for c in cosmosContainers: {
    parent: cosmosDb
    name: c.name
    properties: {
      resource: {
        id: c.name
        partitionKey: { paths: [c.partitionKey], kind: 'Hash' }
        defaultTtl: c.ttl
      }
    }
  }
]

// ---------- Service Bus ----------
var sbName = take('${baseName}-sb-${uniqueString(resourceGroup().id)}', 50)
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: sbName
  location: location
  sku: { name: 'Standard', tier: 'Standard' }
}

resource workQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBus
  name: 'financial-ai-work'
  properties: {
    maxDeliveryCount: 10
    defaultMessageTimeToLive: 'PT1H'
    deadLetteringOnMessageExpiration: true
  }
}

resource eventsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBus
  name: 'financial-ai-events'
  properties: { defaultMessageTimeToLive: 'PT1H' }
}

resource eventsSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: eventsTopic
  name: 'report-generated'
  properties: {
    maxDeliveryCount: 10
    defaultMessageTimeToLive: 'PT1H'
  }
}

// ---------- Key Vault ----------
var kvName = take('${baseName}-kv-${uniqueString(resourceGroup().id)}', 24)
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource kvCosmos 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'cosmos-connection-string'
  properties: {
    value: 'AccountEndpoint=${cosmos.properties.documentEndpoint};AccountKey=${cosmos.listKeys().primaryMasterKey};'
  }
}

resource kvServiceBus 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'servicebus-connection-string'
  properties: {
    value: listKeys('${serviceBus.id}/AuthorizationRules/RootManageSharedAccessKey', serviceBus.apiVersion).primaryConnectionString
  }
}

resource kvAuth 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'auth-signing-key'
  properties: { value: authSigningKey }
}

// ---------- Managed Identity ----------
resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${baseName}-api-identity'
  location: location
}

// Key Vault Secrets User
resource kvRoleApi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, apiIdentity.id, 'kv-secrets-user')
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// AcrPull
resource acrRoleApi 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, apiIdentity.id, 'acr-pull')
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Container Apps Environment ----------
resource containerEnv 'Microsoft.App/managedEnvironments@2023-11-02-preview' = {
  name: '${baseName}-env-${environment}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ---------- Container App: API ----------
resource apiApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: '${baseName}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: apiIdentity.id
        }
      ]
      secrets: [
        {
          name: 'cosmos-connection'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/cosmos-connection-string'
          identity: apiIdentity.id
        }
        {
          name: 'servicebus-connection'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/servicebus-connection-string'
          identity: apiIdentity.id
        }
        {
          name: 'auth-signing-key'
          keyVaultUrl: '${keyVault.properties.vaultUri}secrets/auth-signing-key'
          identity: apiIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acr.properties.loginServer}/${baseName}-api:${apiImageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
            { name: 'Cosmos__ConnectionString', secretRef: 'cosmos-connection' }
            { name: 'Cosmos__BootstrapContainers', value: 'false' }
            { name: 'ServiceBus__Enabled', value: 'true' }
            { name: 'ServiceBus__ConnectionString', secretRef: 'servicebus-connection' }
            { name: 'Auth__SigningKey', secretRef: 'auth-signing-key' }
            { name: 'Auth__Issuer', value: 'FinancialAI' }
            { name: 'Auth__Audience', value: 'FinancialAI' }
            { name: 'AiService__BaseUrl', value: 'http://${baseName}-ai:8000' }
            { name: 'Cors__AllowedOrigins__0', value: '*' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: 8080 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/readyz', port: 8080 }
              initialDelaySeconds: 15
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

// ---------- Container App: AI ----------
resource aiApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: '${baseName}-ai'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8000
        transport: 'http'
      }
      registries: [
        {
          server: acr.properties.loginServer
          identity: apiIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'ai'
          image: '${acr.properties.loginServer}/${baseName}-ai:${aiImageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'LLM_PROVIDER', value: 'stub' }
            { name: 'PORT', value: '8000' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: 8000 }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/readyz', port: 8000 }
              initialDelaySeconds: 15
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

// ---------- Outputs ----------
output apiUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}'
output apiFqdn string = apiApp.properties.configuration.ingress.fqdn
output aiFqdn string = aiApp.properties.configuration.ingress.fqdn
output acrLoginServer string = acr.properties.loginServer
output acrName string = acr.name
output keyVaultName string = keyVault.name
output cosmosEndpoint string = cosmos.properties.documentEndpoint
output serviceBusNamespace string = serviceBus.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output apiManagedIdentityClientId string = apiIdentity.properties.clientId
