output "resource_group_name" {
  value = azurerm_resource_group.draco.name
}

output "postgres_fqdn" {
  value = azurerm_postgresql_flexible_server.draco.fqdn
}

output "postgres_connection_string" {
  value     = "Host=${azurerm_postgresql_flexible_server.draco.fqdn};Database=${azurerm_postgresql_flexible_server_database.draco.name};Username=${var.postgres_admin_login};Password=${var.postgres_admin_password};SSL Mode=Require;Trust Server Certificate=true"
  sensitive = true
}

output "event_ingestion_secret" {
  value     = random_password.event_ingestion_secret.result
  sensitive = true
}

output "storage_account_name" {
  value = var.enable_storage_account ? azurerm_storage_account.draco[0].name : null
}

output "api_environment" {
  value = {
    ASPNETCORE_ENVIRONMENT       = var.environment == "prod" ? "Production" : "Development"
    DRACO_DB_MAIN_CONNECTION     = "Host=${azurerm_postgresql_flexible_server.draco.fqdn};Database=${azurerm_postgresql_flexible_server_database.draco.name};Username=${var.postgres_admin_login};Password=${var.postgres_admin_password};SSL Mode=Require;Trust Server Certificate=true"
    DRACO_EVENT_INGESTION_SECRET = random_password.event_ingestion_secret.result
  }
  sensitive = true
}
