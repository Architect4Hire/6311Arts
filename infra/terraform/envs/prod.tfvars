# Production. Everything here is sized for "it must not lose data and must not sign people out".
#
#   terraform init -backend-config=envs/prod.backend.hcl
#   terraform plan -var-file=envs/prod.tfvars -out prod.tfplan
#
# An apply against prod runs behind the GitHub `prod` environment's approval gate (prompt 6.3) — the
# federated credential's subject is the environment, so there is no way to apply without passing it.

environment = "prod"

subscription_id     = "00000000-0000-0000-0000-000000000000"
resource_group_name = "rg-6311arts-prod"
unique_suffix       = "CHANGEME"

sql_admin_group_name      = "6311arts SQL Admins"
sql_admin_group_object_id = "00000000-0000-0000-0000-000000000000"

key_vault_admin_object_ids = []

# Serverless still, but never paused: the site answers a request at any hour, and a cold start on a paused
# database is tens of seconds.
sql_sku_name                    = "GP_S_Gen5_2"
sql_min_capacity                = 1
sql_auto_pause_delay_in_minutes = -1
sql_max_size_gb                 = 64
sql_short_term_retention_days   = 35

# Weekly and monthly copies beyond the 35-day PITR window. The audit log is immutable by design (ADR 0017),
# so these are the only way back from a bad import or a mistaken bulk change.
sql_long_term_retention = {
  weekly_retention  = "P8W"
  monthly_retention = "P12M"
}

# Uploaded artwork images and the Data Protection key ring have no other copy.
storage_replication_type    = "ZRS"
storage_blob_retention_days = 30

# Append-only persistence, written every second, and a replica. Redis holds the session tickets: losing it
# signs every member and every staff user out mid-checkout (ADR 0009).
redis_sku_name                  = "Balanced_B0"
redis_persistence               = "aof"
redis_high_availability_enabled = true

key_vault_purge_protection_enabled   = true
key_vault_soft_delete_retention_days = 90

log_retention_in_days = 90
log_daily_quota_gb    = -1

# Only production ever talks to Square production, and only with credentials set straight into prod's vault
# (CLAUDE.md "Security and payments").
square_mode = "production"

# The only environment with the centre's real inbox (ADR 0047).
forms_staff_email = "staff@6311arts.com"
seed_admin_email  = "admin@6311arts.com"

# Terraform creates this domain unverified. Publish the TXT, SPF and DKIM records it reports before the
# cutover, or mail keeps leaving from the Azure-managed domain (prompt 6.3).
email_custom_domain = "6311arts.com"

# The SSR engine renders for these hosts and no others (ADR 0046). Add the Container Apps domain until the
# custom domain is live on the gateway.
site_allowed_hosts = ["6311arts.com", "www.6311arts.com"]

tags = {
  costCentre = "prod"
}
