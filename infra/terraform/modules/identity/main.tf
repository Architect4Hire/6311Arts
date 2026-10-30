# One user-assigned managed identity per application, and nothing beyond what that application needs
# (ADR 0008, "a managed identity per app"). The deploy step binds each Container App to the identity named
# for it, so the admin SPA cannot read a secret and the site cannot write a blob.
#
# The pairing with the AppHost is by name: these are the logical names in .claude/rules/aspire.md. Adding an
# application to the model means adding it to the `apps` map in the root module's locals.

resource "azurerm_user_assigned_identity" "app" {
  for_each = var.apps

  name                = "id-${var.prefix}-${each.key}"
  resource_group_name = var.resource_group_name
  location            = var.location

  tags = merge(var.tags, { app = each.key })
}

# Every application pulls its own image. Without this the revision fails to start with an unauthenticated
# pull, which is a confusing way to find out about a missing role assignment.
resource "azurerm_role_assignment" "acr_pull" {
  for_each = var.apps

  scope                = var.container_registry_id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
  principal_type       = "ServicePrincipal"
  description          = "${each.key} pulls its own image"
}

# The media container and the shared Data Protection key ring. Contributor rather than Reader because the
# API writes uploads and renditions, and the key ring is written by whichever host rotates a key first
# (ADR 0023, ADR 0030).
resource "azurerm_role_assignment" "blob_contributor" {
  for_each = { for name, app in var.apps : name => app if app.blobs }

  scope                = var.storage_account_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
  principal_type       = "ServicePrincipal"
  description          = "${each.key} reads and writes media and the Data Protection key ring"
}

# Secrets User, not Officer: an application reads the secret values it was given and can change none of
# them. Setting a value is a person's job, through scripts/set-secrets.sh.
resource "azurerm_role_assignment" "key_vault_secrets_user" {
  for_each = { for name, app in var.apps : name => app if app.secrets }

  scope                = var.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.app[each.key].principal_id
  principal_type       = "ServicePrincipal"
  description          = "${each.key} resolves its own Key Vault references"
}
