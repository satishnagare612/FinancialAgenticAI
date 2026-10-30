# Azure infrastructure (Bicep)

Provisions all resources required by FinancialAI in a single resource group.

## One-time setup

    az login
    az account set --subscription "<subscription-id>"
    az group create --name financialai-staging --location eastus

    # Entra ID app + federated credential for GitHub Actions
    az ad app create --display-name "FinancialAI-GitHub"
    az ad sp create --id <appId>

    az ad app federated-credential create --id <appId> \
      --parameters '{"name":"github-main","issuer":"https://token.actions.githubusercontent.com","subject":"repo:satishnagare612/FinancialAgenticAI:ref:refs/heads/main","audiences":["api://AzureADTokenExchange"]}'

    az role assignment create --assignee <appId> --role Contributor \
      --scope /subscriptions/<sub>/resourceGroups/financialai-staging
    az role assignment create --assignee <appId> --role "User Access Administrator" \
      --scope /subscriptions/<sub>/resourceGroups/financialai-staging

## Deploy

    # Validate syntax first
    az bicep build --file infra/bicep/main.bicep

    # Deploy
    az deployment group create \
      --resource-group financialai-staging \
      --template-file infra/bicep/main.bicep \
      --parameters infra/bicep/main.parameters.json \
      --parameters authSigningKey="$(openssl rand -base64 32)"

    # Read outputs
    az deployment group show --resource-group financialai-staging \
      --name main --query properties.outputs

## What gets provisioned

- Log Analytics + Application Insights
- Azure Container Registry (Basic)
- Cosmos DB (Serverless, SQL API) with 5 containers
- Service Bus (Standard): 1 queue, 1 topic, 1 subscription
- Key Vault with 3 secrets (cosmos, servicebus, auth)
- User-assigned managed identity: Key Vault Secrets User + AcrPull
- Container Apps Environment
- API Container App (external ingress, port 8080)
- AI Container App (internal ingress, port 8000)

## Cost at idle

Roughly $15–$30/month:
- Cosmos Serverless: $0–$5
- Service Bus Standard: ~$10
- ACR Basic: ~$5
- App Insights: $0–$5
- Container Apps: $0–$5

## Production notes

- `EnableServerless` on Cosmos does not support multi-region or high-throughput workloads. Switch to `Standard` + autoscale for production scale.
- `Cors__AllowedOrigins__0` is `*`. Override for production.
- AI Container App ships with `LLM_PROVIDER=stub`. For real model: add Key Vault secret `llm-api-key`, change to `real` + `secretRef`, add `LLM_PRESET`.
- `minReplicas: 1` avoids cold starts. Set to `0` for cost at the expense of ~10 s cold start.
