# Azure Communication Services email. Every email in the system is an outbox message dispatched by
# Arts6311.Worker, which is the only host that sends one (ADR 0023) — password resets, email confirmations,
# order and membership mail, and the staff notifications for the public forms (ADR 0047).
#
# Locally this is Mailpit and nothing leaves the laptop. Here it is a real sender, which is exactly why the
# default is the Azure-managed domain: it sends from donotreply@<guid>.azurecomm.net and is rate limited, so
# a confirmation triggered in dev cannot be mistaken for one from the centre.

resource "azurerm_communication_service" "main" {
  name                = var.names.communication
  resource_group_name = var.resource_group_name

  # Required in azurerm 5.x; it no longer defaults to "United States".
  data_location = var.data_location

  tags = var.tags
}

resource "azurerm_email_communication_service" "main" {
  name                = var.names.email_service
  resource_group_name = var.resource_group_name
  data_location       = var.data_location

  tags = var.tags
}

resource "azurerm_email_communication_service_domain" "azure_managed" {
  # The name is fixed by the service for a managed domain.
  name              = "AzureManagedDomain"
  email_service_id  = azurerm_email_communication_service.main.id
  domain_management = "AzureManaged"

  # Open and click tracking off: this is transactional mail about somebody's order, class or password.
  user_engagement_tracking_enabled = false

  tags = var.tags
}

resource "azurerm_communication_service_email_domain_association" "azure_managed" {
  communication_service_id = azurerm_communication_service.main.id
  email_service_domain_id  = azurerm_email_communication_service_domain.azure_managed.id
}

# Production only, and only once DNS is ready. Terraform creates the domain in an unverified state; the TXT,
# SPF and DKIM records it asks for have to be published on the real domain before anything will send
# through it (prompt 6.3). Until then the managed domain above keeps working.
resource "azurerm_email_communication_service_domain" "custom" {
  count = var.custom_domain == null ? 0 : 1

  name              = var.custom_domain
  email_service_id  = azurerm_email_communication_service.main.id
  domain_management = "CustomerManaged"

  user_engagement_tracking_enabled = false

  tags = var.tags
}

resource "azurerm_communication_service_email_domain_association" "custom" {
  count = var.custom_domain == null ? 0 : 1

  communication_service_id = azurerm_communication_service.main.id
  email_service_domain_id  = azurerm_email_communication_service_domain.custom[0].id
}
