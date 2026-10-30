# Every assignment below is scoped to one resource group or one blob container. Nothing is subscription-wide:
# that is the whole reason the resource groups are created in bootstrap rather than by the module CI runs.

locals {
  # Roles the deploy identity needs on its own resource group, and why each one is unavoidable.
  deploy_roles = {
    # Create and change the resources in the group.
    "Contributor" = "manage the resources in the group"

    # The root module assigns roles itself (each app identity's AcrPull, blob and Key Vault access), and
    # assigning a role requires rights to write role assignments. Scoped here, so the identity can grant
    # access inside its own group and nowhere else.
    "Role Based Access Control Administrator" = "assign the app identities their own roles"

    # Contributor is a control-plane role: it can create a key vault but not write a secret into one, and
    # the vault uses RBAC rather than access policies.
    "Key Vault Secrets Officer" = "create the secret shells the apps read"

    # Also data-plane, also not covered by Contributor: pushing an image to the registry.
    "AcrPush" = "push the application images"
  }
}

resource "azurerm_role_assignment" "deploy" {
  for_each = {
    for pair in setproduct(tolist(var.environments), keys(local.deploy_roles)) :
    "${pair[0]}:${pair[1]}" => { environment = pair[0], role = pair[1] }
  }

  scope                = azurerm_resource_group.env[each.value.environment].id
  role_definition_name = each.value.role
  principal_id         = azurerm_user_assigned_identity.deploy[each.value.environment].principal_id
  principal_type       = "ServicePrincipal"
  description          = "CI deploy identity for ${each.value.environment}: ${local.deploy_roles[each.value.role]}"
}

resource "azurerm_role_assignment" "plan_reader" {
  for_each = var.environments

  scope                = azurerm_resource_group.env[each.key].id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.plan[each.key].principal_id
  principal_type       = "ServicePrincipal"
  description          = "Pull-request plan identity for ${each.key}: read the current state of the group, change nothing"
}

# State access. Scoped to the container, not the account: Storage Blob Data Contributor is what both reading
# the state file and taking its lease need. The plan identity gets it too — a plan takes the same lease.
resource "azurerm_role_assignment" "deploy_state" {
  for_each = var.environments

  scope                = azurerm_storage_container.state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.deploy[each.key].principal_id
  principal_type       = "ServicePrincipal"
  description          = "Read, write and lease ${each.key}'s state file"
}

resource "azurerm_role_assignment" "plan_state" {
  for_each = var.environments

  scope                = azurerm_storage_container.state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.plan[each.key].principal_id
  principal_type       = "ServicePrincipal"
  description          = "Read and lease ${each.key}'s state file during a pull-request plan"
}
