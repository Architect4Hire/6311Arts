locals {
  prefix = "${var.project}-${var.environment}"

  tags = merge(
    {
      project     = var.project
      environment = var.environment
      component   = "infrastructure"
      managedBy   = "terraform"
    },
    var.tags,
  )

  # Globally unique names, built from unique_suffix so two subscriptions can hold the same environment.
  # The rules differ per service, which is why these are not one expression:
  #   storage account   3-24, lowercase letters and digits only
  #   container registry 5-50, letters and digits only
  #   key vault          3-24, must start with a letter, letters digits and hyphens
  names = {
    container_registry   = "acr${var.project}${var.environment}${var.unique_suffix}"
    key_vault            = "kv-${var.project}-${var.environment}-${var.unique_suffix}"
    storage_account      = "st${var.project}${var.environment}${var.unique_suffix}"
    sql_server           = "sql-${local.prefix}-${var.unique_suffix}"
    redis                = "redis-${local.prefix}-${var.unique_suffix}"
    log_analytics        = "log-${local.prefix}"
    application_insights = "appi-${local.prefix}"
    container_apps_env   = "cae-${local.prefix}"
    communication        = "acs-${local.prefix}"
    email_service        = "acs-email-${local.prefix}"
  }

  # One user-assigned identity per application, matching the logical resource names the AppHost uses
  # (.claude/rules/aspire.md, "Stable logical names"). The deploy step binds each Container App to the
  # identity named for it, so an app can only reach what its own identity was granted.
  #
  #   blobs   - the media container and the shared Data Protection key ring (ADR 0023, ADR 0030)
  #   sql     - needs a database user created for it by scripts/grant-sql-identities.sh
  #   secrets - reads Key Vault references
  apps = {
    gateway    = { blobs = false, sql = false, secrets = true }
    api        = { blobs = true, sql = true, secrets = true }
    site       = { blobs = false, sql = false, secrets = true }
    admin      = { blobs = false, sql = false, secrets = false }
    worker     = { blobs = true, sql = true, secrets = true }
    migrations = { blobs = true, sql = true, secrets = true }
  }
}
