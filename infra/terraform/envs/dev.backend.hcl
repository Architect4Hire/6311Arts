# Where dev's state lives. Fill storage_account_name in from `terraform output state_storage_account_name`
# in bootstrap; the rest is fixed.
#
#   terraform init -backend-config=envs/dev.backend.hcl
#
# Nothing here is secret. Credentials come from `az login` on a laptop, or from ARM_USE_OIDC plus the
# ARM_CLIENT_ID / ARM_TENANT_ID / ARM_SUBSCRIPTION_ID environment variables in CI.
resource_group_name  = "rg-6311arts-tfstate"
storage_account_name = "st6311artstfstateCHANGEME"
container_name       = "tfstate"
key                  = "dev.tfstate"

# The account has shared-key access switched off, so the data plane is reached with Entra ID.
use_azuread_auth = true
