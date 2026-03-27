locals {
  name_prefix = lower(replace("${var.project_name}-${var.environment}", "/[^a-zA-Z0-9-]/", ""))
  common_tags = merge(
    {
      project     = var.project_name
      environment = var.environment
      managed_by  = "terraform"
    },
    var.tags
  )
}

resource "random_string" "suffix" {
  length  = 5
  upper   = false
  numeric = true
  special = false
}

resource "random_password" "event_ingestion_secret" {
  length           = 32
  special          = true
  override_special = "!#$%&*()-_=+[]{}<>:?"
}

resource "azurerm_resource_group" "draco" {
  name     = "${local.name_prefix}-rg"
  location = var.location
  tags     = local.common_tags
}

resource "azurerm_postgresql_flexible_server" "draco" {
  name                          = substr("${replace(local.name_prefix, "-", "")}${random_string.suffix.result}", 0, 63)
  resource_group_name           = azurerm_resource_group.draco.name
  location                      = azurerm_resource_group.draco.location
  version                       = var.postgres_version
  administrator_login           = var.postgres_admin_login
  administrator_password        = var.postgres_admin_password
  sku_name                      = var.postgres_sku_name
  storage_mb                    = var.postgres_storage_mb
  backup_retention_days         = var.postgres_backup_retention_days
  geo_redundant_backup_enabled  = false
  public_network_access_enabled = true
  tags                          = local.common_tags
}

resource "azurerm_postgresql_flexible_server_database" "draco" {
  name      = var.postgres_database_name
  server_id = azurerm_postgresql_flexible_server.draco.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "azure_services" {
  count            = var.allow_azure_services ? 1 : 0
  name             = "allow-azure-services"
  server_id        = azurerm_postgresql_flexible_server.draco.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_postgresql_flexible_server_firewall_rule" "allowed_ips" {
  for_each         = { for ip in var.allowed_ip_addresses : replace(ip, ".", "-") => ip }
  name             = "client-${each.key}"
  server_id        = azurerm_postgresql_flexible_server.draco.id
  start_ip_address = each.value
  end_ip_address   = each.value
}

resource "azurerm_storage_account" "draco" {
  count                    = var.enable_storage_account ? 1 : 0
  name                     = substr(replace("${var.project_name}${var.environment}${random_string.suffix.result}", "/[^a-zA-Z0-9]/", ""), 0, 24)
  resource_group_name      = azurerm_resource_group.draco.name
  location                 = azurerm_resource_group.draco.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"
  tags                     = local.common_tags
}

resource "azurerm_storage_container" "artifacts" {
  count                 = var.enable_storage_account ? 1 : 0
  name                  = "draco-artifacts"
  storage_account_id    = azurerm_storage_account.draco[0].id
  container_access_type = "private"
}
