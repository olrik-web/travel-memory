// Travel Memory in Azure at close to zero cost: everything scales to zero or runs on a free
// offer, and nothing has a fixed monthly price. Deployed to one resource group by the
// deploy workflow; see the README for the one-time setup.

targetScope = 'resourceGroup'

@description('Region for all resources. Defaults to the resource group\'s region.')
param location string = resourceGroup().location

@description('API image, including the SPA, for example ghcr.io/olrik-web/travel-memory-api:<sha>.')
param apiImage string

@description('Worker image, for example ghcr.io/olrik-web/travel-memory-worker:<sha>.')
param workerImage string

@description('OpenID Connect authority of the Entra External ID tenant.')
param oidcAuthority string

@description('Client id of the web app registration in the Entra External ID tenant.')
param oidcClientId string

@secure()
@description('Client secret of the web app registration in the Entra External ID tenant.')
param oidcClientSecret string

@description('Monthly budget for the resource group, in the billing currency.')
param budgetAmount int = 10

@description('Email address for budget alerts.')
param budgetContactEmail string

@description('First day of the budget period. It must be the first of a month and never changes afterwards.')
param budgetStartDate string = '2026-09-01'

// The region is part of the suffix, so moving to another region creates fresh names
// instead of colliding with resources from the old region that are still being deleted or
// are soft-deleted, such as a SQL server name or a Log Analytics workspace.
var suffix = uniqueString(resourceGroup().id, location)
var databaseName = 'travelmemory'
var processingQueueName = 'photo-processing'
var blobContainerNames = [
  'photo-imports'
  'photos'
  'data-protection'
]

// Built-in role definition ids.
var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageQueueDataContributor = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'

// One identity for the API, the worker, and the jobs. It can read and write the photo
// containers and the queue, sign user delegation SAS tokens, and administer the database.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-travel-memory'
  location: location
}

// Ingestion is capped per day so the logs stay inside the free monthly allowance.
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-travel-memory-${suffix}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json('0.1')
    }
  }
}

// Shared key access is disabled: the apps use their managed identity, and SAS tokens are
// signed with a user delegation key.
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${suffix}'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// The browser uploads originals and reads derivatives directly with SAS URLs, so Blob
// Storage accepts cross-origin requests from the app, and only from the app.
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    cors: {
      corsRules: [
        {
          allowedOrigins: [
            'https://${api.properties.configuration.ingress.fqdn}'
          ]
          allowedMethods: [
            'GET'
            'PUT'
            'OPTIONS'
          ]
          allowedHeaders: [
            '*'
          ]
          exposedHeaders: [
            'ETag'
            'x-ms-request-id'
          ]
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

resource blobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for name in blobContainerNames: {
    parent: blobService
    name: name
    properties: {
      publicAccess: 'None'
    }
  }
]

// A safety net for originals the database no longer knows about, such as an upload a
// browser finished after its trip was deleted. Failed originals are kept for 7 days at
// most, so nothing older than 8 days in this container is still needed. Lifecycle
// management itself is free.
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'expire-stray-originals'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                'photo-imports/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 8
                }
              }
            }
          }
        }
      ]
    }
  }
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource processingQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: processingQueueName
}

resource blobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      storageBlobDataContributor
    )
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource queueAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, storageQueueDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      storageQueueDataContributor
    )
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Microsoft Entra authentication only, with the app identity as administrator, so no SQL
// password exists and no T-SQL is needed to grant the apps access. Container Apps on the
// Consumption plan has no fixed outbound addresses, so Azure services may connect.
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-travel-memory-${suffix}'
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: identity.name
      sid: identity.properties.principalId
      tenantId: subscription().tenantId
      principalType: 'Application'
    }
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// The Azure SQL free offer: 100,000 vCore seconds and 32 GB a month. When the free amount
// is used up, the database pauses until the next month instead of billing.
resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

resource environment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-travel-memory-${suffix}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// Settings shared by the API, the worker, and the jobs. AZURE_CLIENT_ID tells the Azure SDK
// and SqlClient which managed identity to use.
var sharedEnvironment = [
  {
    name: 'AZURE_CLIENT_ID'
    value: identity.properties.clientId
  }
  {
    name: 'ConnectionStrings__travelmemory'
    value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Default;Encrypt=True;'
  }
  {
    name: 'ConnectionStrings__blobs'
    value: storage.properties.primaryEndpoints.blob
  }
  {
    name: 'ConnectionStrings__queues'
    value: storage.properties.primaryEndpoints.queue
  }
]

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-travel-memory-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
      }
      secrets: [
        {
          name: 'oidc-client-secret'
          value: oidcClientSecret
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: concat(sharedEnvironment, [
            {
              // Container Apps terminates TLS, so the API must trust X-Forwarded-Proto to
              // build https callback URLs and to issue its Secure cookies.
              name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
              value: 'true'
            }
            {
              name: 'Authentication__Oidc__Authority'
              value: oidcAuthority
            }
            {
              name: 'Authentication__Oidc__ClientId'
              value: oidcClientId
            }
            {
              name: 'Authentication__Oidc__ClientSecret'
              secretRef: 'oidc-client-secret'
            }
          ])
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

// Woken by queue messages and back to zero replicas once the queue has been empty for the
// cooldown period, so neither the worker nor the database stays awake between imports.
resource worker 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'ca-travel-memory-worker'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    template: {
      containers: [
        {
          name: 'worker'
          image: workerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: sharedEnvironment
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'photo-processing-queue'
            azureQueue: {
              accountName: storage.name
              queueName: processingQueueName
              queueLength: 1
              identity: identity.id
            }
          }
        ]
      }
    }
  }
}

// Housekeeping while the worker is scaled to zero: one maintenance cycle a day.
resource maintenanceJob 'Microsoft.App/jobs@2025-01-01' = {
  name: 'caj-travel-memory-maintenance'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 600
      replicaRetryLimit: 1
      scheduleTriggerConfig: {
        cronExpression: '0 3 * * *'
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'maintenance'
          image: workerImage
          args: [
            '--maintenance'
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: sharedEnvironment
        }
      ]
    }
  }
}

// Started by the deploy workflow after each deployment to apply pending migrations.
resource migrationJob 'Microsoft.App/jobs@2025-01-01' = {
  name: 'caj-travel-memory-migrate'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: apiImage
          args: [
            '--migrate'
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: sharedEnvironment
        }
      ]
    }
  }
}

// Only an alert, not a cap: pay-as-you-go subscriptions cannot stop spending automatically.
resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-travel-memory'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
    }
    notifications: {
      actualHalf: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [
          budgetContactEmail
        ]
      }
      actualFull: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [
          budgetContactEmail
        ]
      }
      forecastFull: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [
          budgetContactEmail
        ]
      }
    }
  }
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output migrationJobName string = migrationJob.name
