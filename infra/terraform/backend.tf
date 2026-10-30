# Partial configuration on purpose. The storage coordinates differ per environment only in `key`, and the
# credentials differ between a laptop and a runner, so both arrive at init time:
#
#   terraform init -backend-config=envs/dev.backend.hcl
#
# Nothing here is a secret, and no cloud credential is stored in the repository or in GitHub: a laptop
# authenticates through `az login`, and CI exchanges its GitHub OIDC token (ARM_USE_OIDC=true with the
# ARM_CLIENT_ID / ARM_TENANT_ID / ARM_SUBSCRIPTION_ID bootstrap printed).
terraform {
  backend "azurerm" {}
}
