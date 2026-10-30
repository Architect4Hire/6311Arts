# Every secret the applications read, as Key Vault references resolved by their own managed identity.
#
# Two kinds live here, and the difference matters:
#
#   1. Values Azure itself produces (the Application Insights connection string, the Redis access key, the
#      Communication Services key). Terraform has to read these to write them, so they are in Terraform
#      state. That is why the state container is private, Entra-only and has shared-key access switched off.
#
#   2. Values the application generates on a laptop (the ES256 signing pair, the idempotency HMAC key, the
#      seeded admin password, the SSR credential) and the Square credentials a person holds. Terraform
#      creates an empty *shell* for each of these and never learns the value: `ignore_changes` on `value`
#      means a value set out of band by scripts/set-secrets.sh is left alone for ever after. No application
#      secret ever enters Terraform state, a plan, a log or a workflow (CLAUDE.md "Environments and
#      secrets").
#
# The placeholder is deliberately not a weak-but-valid value. A revision deployed before the real secrets
# are set fails to start — the gateway cannot parse "REPLACE-ME" as a PEM — rather than quietly signing
# internal tokens with something guessable.

locals {
  secret_placeholder = "REPLACE-ME see docs/infrastructure/README.md"

  # name => what it is, and which application reads it. Setting all of them is one run of
  # scripts/set-secrets.sh.
  secret_shells = {
    "internal-token-signing-key"   = "ES256 private key PEM (PKCS#8). The gateway signs the internal token with it (ADR 0002). The gateway alone reads it."
    "internal-token-public-key"    = "The SubjectPublicKeyInfo PEM derived from the signing key. The API verifies with it and must never see the private half."
    "idempotency-fingerprint-key"  = "Base64 of 32 random bytes. Keys the HMAC of idempotent request fingerprints (ADR 0020). The API alone reads it."
    "seed-admin-password"          = "Password for the first Admin that MigrationService seeds. The migrations job alone reads it."
    "site-ssr-credential"          = "The credential the site presents on its server-side reads so the gateway gives them their own rate-limit partition (ADR 0046, prompt 6.1). Never sent to a browser."
    "square-access-token"          = "Square access token for this environment's mode. Sandbox outside production; production only in prod (CLAUDE.md 'Run mode never reaches Square production')."
    "square-webhook-signature-key" = "Square webhook signature key. The API verifies every webhook with it before the inbox row is written."
  }
}

resource "azurerm_key_vault_secret" "shell" {
  for_each = local.secret_shells

  name         = each.key
  key_vault_id = module.platform.key_vault_id
  value        = local.secret_placeholder
  content_type = each.value

  lifecycle {
    ignore_changes = [value]
  }
}

# --------------------------------------------------------------------------------------------------------
# Connection strings
# --------------------------------------------------------------------------------------------------------
# Named to match what the applications read: Aspire's configuration keys are ConnectionStrings__<name>, and
# the logical names are the AppHost's (appdb, blobs, cache, mail). Keeping them in the vault means the
# deploy step sets one kind of thing — a Key Vault reference — for every one of them.

# No password. The server is Entra-authentication-only, and `Active Directory Default` plus the app's own
# AZURE_CLIENT_ID is what signs it in. The database user each identity signs in as is created by
# scripts/grant-sql-identities.sh; until that has run, a connection fails with "login failed for user
# '<token-identified principal>'".
resource "azurerm_key_vault_secret" "appdb" {
  name         = "connectionstrings-appdb"
  key_vault_id = module.platform.key_vault_id
  content_type = "ConnectionStrings__appdb"

  value = join(";", [
    "Server=tcp:${module.data.sql_server_fqdn},1433",
    "Initial Catalog=${module.data.sql_database_name}",
    "Authentication=Active Directory Default",
    "Encrypt=True",
    "TrustServerCertificate=False",
    "Connection Timeout=30",
  ])
}

# A service URI, not a credential: Aspire's blob client pairs it with the app's managed identity.
resource "azurerm_key_vault_secret" "blobs" {
  name         = "connectionstrings-blobs"
  key_vault_id = module.platform.key_vault_id
  content_type = "ConnectionStrings__blobs"
  value        = module.data.blob_service_uri
}

resource "azurerm_key_vault_secret" "cache" {
  name         = "connectionstrings-cache"
  key_vault_id = module.platform.key_vault_id
  content_type = "ConnectionStrings__cache"
  value        = module.data.redis_connection_string
}

# The Worker's only sender (ADR 0023). Note that Arts6311.Worker speaks SMTP today, against Mailpit, and
# this is an Azure Communication Services connection string: see docs/infrastructure/README.md, "Gaps in
# the application", before the first environment is expected to send mail.
resource "azurerm_key_vault_secret" "mail" {
  name         = "connectionstrings-mail"
  key_vault_id = module.platform.key_vault_id
  content_type = "ConnectionStrings__mail"
  value        = module.email.connection_string
}

resource "azurerm_key_vault_secret" "application_insights" {
  name         = "applicationinsights-connection-string"
  key_vault_id = module.platform.key_vault_id
  content_type = "APPLICATIONINSIGHTS_CONNECTION_STRING"
  value        = module.platform.application_insights_connection_string
}
