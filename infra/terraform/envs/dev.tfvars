# Dev: cheap, disposable, scales down, and never sends mail to a real person.
#
#   terraform init -backend-config=envs/dev.backend.hcl
#   terraform plan -var-file=envs/dev.tfvars -out dev.tfplan

environment = "dev"

# From bootstrap: `terraform output -json resource_group_names`. The same unique_suffix bootstrap used.
subscription_id     = "00000000-0000-0000-0000-000000000000"
resource_group_name = "rg-6311arts-dev"
unique_suffix       = "CHANGEME"

# The Entra group that administers the SQL server. The server is Entra-only, so members of this group are
# the only ones who can create the database users the applications sign in as.
#   az ad group show --group "6311arts SQL Admins" --query id -o tsv
sql_admin_group_name      = "6311arts SQL Admins"
sql_admin_group_object_id = "00000000-0000-0000-0000-000000000000"

# Whoever runs scripts/set-secrets.sh. Your own object id is enough to start:
#   az ad signed-in-user show --query id -o tsv
key_vault_admin_object_ids = []

# Serverless, paused after an hour idle. Note that Arts6311.Worker polls the outbox continuously, so the
# database only actually pauses while the worker is scaled to zero.
sql_sku_name                    = "GP_S_Gen5_1"
sql_min_capacity                = 0.5
sql_auto_pause_delay_in_minutes = 60
sql_short_term_retention_days   = 7

storage_replication_type = "LRS"

# Smallest Managed Redis, no persistence: a restart signs dev's test users out, which is fine.
redis_sku_name                  = "Balanced_B0"
redis_persistence               = "none"
redis_high_availability_enabled = false

# Dev can be torn down and rebuilt, and purge protection would hold the vault name for days afterwards.
key_vault_purge_protection_enabled   = false
key_vault_soft_delete_retention_days = 7

log_retention_in_days = 30
log_daily_quota_gb    = 1

# Square sandbox, never production (CLAUDE.md "Run mode never needs an Azure subscription and never reaches
# Square production").
square_mode = "sandbox"

# Non-routable, so a visitor's form confirmation from dev cannot reach a stranger (ADR 0047). Combined with
# the Azure-managed sending domain, which is rate limited and sends from azurecomm.net.
forms_staff_email = "staff@6311arts.localhost"
seed_admin_email  = "admin@6311arts.localhost"
email_custom_domain = null

# Empty means "the Container Apps domain only", computed from the environment (ADR 0046).
site_allowed_hosts = []

tags = {
  costCentre = "dev"
}
