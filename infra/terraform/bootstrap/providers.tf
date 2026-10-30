provider "azurerm" {
  subscription_id = var.subscription_id

  # azurerm 5.x registers nothing by default (it registered ~60 providers in 4.x). Registration is a
  # subscription-level action, and the CI identities are scoped to a single resource group, so they cannot
  # do it themselves — every provider the root module needs is registered here instead, in the run a person
  # makes with their own credentials. Registration is idempotent, so re-running bootstrap is harmless.
  #
  # Container Apps and jobs; role assignments; Managed Redis; Communication Services email; the image
  # registry; Application Insights; Key Vault; the per-app and CI identities; Log Analytics; Azure SQL;
  # the state account and the media/dataprotection account.
  resource_providers_to_register = [
    "Microsoft.App",
    "Microsoft.Authorization",
    "Microsoft.Cache",
    "Microsoft.Communication",
    "Microsoft.ContainerRegistry",
    "Microsoft.Insights",
    "Microsoft.KeyVault",
    "Microsoft.ManagedIdentity",
    "Microsoft.OperationalInsights",
    "Microsoft.Sql",
    "Microsoft.Storage",
  ]

  # The state container is written over the data plane with Entra ID, not a shared key — the account has
  # shared-key access switched off entirely.
  storage_use_azuread = true

  features {}
}
