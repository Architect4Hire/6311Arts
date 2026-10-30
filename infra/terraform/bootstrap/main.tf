# Bootstrap: the handful of resources that have to exist before anything else can be managed from CI.
#
# Run once, by a person, with their own privileged credentials (`az login`). It creates the remote state
# account, the resource group each environment lives in, and the GitHub OIDC identities CI authenticates as.
# Everything else — the Container Apps environment, SQL, storage, Key Vault, Redis, email — belongs to the
# root module one directory up, which keeps its state in the account created here.
#
# Resource groups are created here rather than there on purpose: it is what lets the CI identities be scoped
# to a single resource group instead of the whole subscription (CLAUDE.md "Environments and secrets").

data "azurerm_client_config" "current" {}

locals {
  tags = merge(
    {
      project   = var.project
      component = "infrastructure-bootstrap"
      managedBy = "terraform"
    },
    var.tags,
  )

  # The GitHub OIDC issuer. Fixed by GitHub, the same for every repository.
  github_issuer = "https://token.actions.githubusercontent.com"
}

# ---------------------------------------------------------------------------------------------------------
# Remote state
# ---------------------------------------------------------------------------------------------------------

resource "azurerm_resource_group" "state" {
  name     = "rg-${var.project}-tfstate"
  location = var.location
  tags     = local.tags
}

resource "azurerm_storage_account" "state" {
  name                = "st${var.project}tfstate${var.unique_suffix}"
  resource_group_name = azurerm_resource_group.state.name
  location            = azurerm_resource_group.state.location

  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = "GRS" # state is the one thing here that cannot be rebuilt from the repository

  https_traffic_only_enabled      = true
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false

  # No shared keys and no SAS: the backend authenticates with Entra ID (`use_azuread_auth = true` in
  # envs/*.backend.hcl), and so does the provider (`storage_use_azuread = true`). A leaked account key is
  # then not a thing that exists. Terraform state holds resource attributes — among them the Managed Redis
  # access key — so read access to this container is read access to secrets.
  shared_access_key_enabled       = false
  default_to_oauth_authentication = true

  # Reachable from the internet, which is how a laptop and a GitHub runner both reach it. Narrowing this to
  # a private endpoint means giving the runners a route in; that is a decision for later, not a default.
  public_network_access = "Enabled"

  blob_properties {
    # Versioning plus soft delete is the recovery story for state: a bad apply or a truncated write can be
    # rolled back to the previous blob version instead of reconstructed by hand.
    versioning_enabled = true

    delete_retention_policy {
      days = 30
    }

    container_delete_retention_policy {
      days = 30
    }
  }

  tags = local.tags
}

resource "azurerm_storage_container" "state" {
  name               = var.state_container_name
  storage_account_id = azurerm_storage_account.state.id

  # Never public. The root module's own blobs (media) are private for the same reason.
  container_access_type = "private"
}

# ---------------------------------------------------------------------------------------------------------
# One resource group per environment
# ---------------------------------------------------------------------------------------------------------

resource "azurerm_resource_group" "env" {
  for_each = var.environments

  name     = "rg-${var.project}-${each.key}"
  location = var.location

  tags = merge(local.tags, {
    environment = each.key
    component   = "infrastructure"
  })
}

# ---------------------------------------------------------------------------------------------------------
# GitHub OIDC: one identity that may change an environment, one that may only read it
# ---------------------------------------------------------------------------------------------------------
# No cloud credential is ever stored in GitHub (prompt 6.2). A workflow exchanges its own short-lived OIDC
# token for an Azure token, and the federated credential's subject decides which workflow may do it.

resource "azurerm_user_assigned_identity" "deploy" {
  for_each = var.environments

  name                = "id-${var.project}-gh-${each.key}-deploy"
  resource_group_name = azurerm_resource_group.env[each.key].name
  location            = azurerm_resource_group.env[each.key].location
  tags                = merge(local.tags, { environment = each.key })
}

# Subject `environment:<name>`: the token is only issued to a job that declares `environment: <name>`, which
# is also where GitHub's own approval gate lives. That is what puts a human in front of a prod apply (6.3).
resource "azurerm_federated_identity_credential" "deploy" {
  for_each = var.environments

  # No resource_group_name: azurerm 5.x derives it from the identity (`parent_id` was removed in favour of
  # `user_assigned_identity_id`).
  name                      = "github-environment-${each.key}"
  user_assigned_identity_id = azurerm_user_assigned_identity.deploy[each.key].id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = local.github_issuer
  subject                   = "repo:${var.github_repository}:environment:${each.key}"
}

resource "azurerm_user_assigned_identity" "plan" {
  for_each = var.environments

  name                = "id-${var.project}-gh-${each.key}-plan"
  resource_group_name = azurerm_resource_group.env[each.key].name
  location            = azurerm_resource_group.env[each.key].location
  tags                = merge(local.tags, { environment = each.key })
}

# Pull requests get a read-only identity, so `terraform plan` can run on a PR without the PR being able to
# change anything in Azure. A fork's workflow runs under the fork's own repository, so its token's subject
# never matches this one.
resource "azurerm_federated_identity_credential" "plan_pull_request" {
  for_each = var.environments

  name                      = "github-pull-request-${each.key}"
  user_assigned_identity_id = azurerm_user_assigned_identity.plan[each.key].id
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = local.github_issuer
  subject                   = "repo:${var.github_repository}:pull_request"
}
