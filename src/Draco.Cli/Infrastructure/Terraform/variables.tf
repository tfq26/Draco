variable "project_name" {
  type        = string
  description = "Base name used for Draco resources."
  default     = "draco"
}

variable "environment" {
  type        = string
  description = "Environment name, such as dev, staging, or prod."
  default     = "dev"
}

variable "location" {
  type        = string
  description = "Azure region for the resource group and database."
  default     = "East US"
}

variable "tags" {
  type        = map(string)
  description = "Additional tags applied to resources."
  default     = {}
}

variable "postgres_admin_login" {
  type        = string
  description = "Administrator username for PostgreSQL."
  default     = "dracoadmin"
}

variable "postgres_admin_password" {
  type        = string
  description = "Administrator password for PostgreSQL."
  sensitive   = true
}

variable "postgres_database_name" {
  type        = string
  description = "Application database name."
  default     = "draco"
}

variable "postgres_version" {
  type        = string
  description = "PostgreSQL major version for Azure Flexible Server."
  default     = "16"
}

variable "postgres_sku_name" {
  type        = string
  description = "Low-cost default SKU for development."
  default     = "B_Standard_B1ms"
}

variable "postgres_storage_mb" {
  type        = number
  description = "Allocated storage for PostgreSQL."
  default     = 32768
}

variable "postgres_backup_retention_days" {
  type        = number
  description = "Backup retention window."
  default     = 7
}

variable "allow_azure_services" {
  type        = bool
  description = "Allow Azure-hosted services to reach the database."
  default     = true
}

variable "allowed_ip_addresses" {
  type        = list(string)
  description = "Public IPs allowed to connect directly to PostgreSQL."
  default     = []
}

variable "enable_storage_account" {
  type        = bool
  description = "Provision a cheap storage account for artifacts and future archives."
  default     = true
}
