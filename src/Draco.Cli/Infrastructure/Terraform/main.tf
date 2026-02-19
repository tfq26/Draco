provider "azurerm" {
  features {}
}

variable "project_name" {
  default = "draco-sentinel"
}

variable "location" {
  default = "East US"
}

resource "azurerm_resource_group" "draco_rg" {
  name     = "${var.project_name}-rg"
  location = var.location
}

# Azure Database for PostgreSQL - Flexible Server
resource "azurerm_postgresql_flexible_server" "draco_db" {
  name                   = "${var.project_name}-db"
  resource_group_name    = azurerm_resource_group.draco_rg.name
  location               = azurerm_resource_group.draco_rg.location
  version                = "13"
  administrator_login    = "dracoadmin"
  administrator_password = "ChangeMe123!" # In a real scenario, use a variable or secret
  storage_mb             = 32768
  sku_name               = "GP_Standard_D2s_v3"
}

# Azure AI Search (for Vector storage/analysis)
resource "azurerm_search_service" "draco_ai" {
  name                = "${var.project_name}-search"
  resource_group_name = azurerm_resource_group.draco_rg.name
  location            = azurerm_resource_group.draco_rg.location
  sku                 = "basic"
}

# Outputs for .env
output "postgres_connection_string" {
  value = "Host=${azurerm_postgresql_flexible_server.draco_db.fqdn};Database=draco;Username=dracoadmin;Password=ChangeMe123!;SSL Mode=Require;"
  sensitive = true
}
