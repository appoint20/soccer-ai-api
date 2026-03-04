resource "random_id" "db_name_suffix" {
  byte_length = 4
}

resource "random_password" "db_password" {
  length           = 16
  special          = true
  override_special = "!#$%&*()-_=+[]{}<>:?"
}

resource "google_sql_database_instance" "postgres" {
  name             = "soccer-ai-pg-${random_id.db_name_suffix.hex}"
  database_version = "POSTGRES_15"
  region           = var.region
  project          = var.project_id

  settings {
    tier = "db-f1-micro"
    
    # We want this for production but for a micro burstable instance,
    # SSD is more expensive but better for concurrent I/O.
    disk_type = "PD_SSD"
    disk_size = 10
    disk_autoresize = true

    # Required to link Cloud Run directly
    ip_configuration {
      ipv4_enabled    = true
      
      # We will allow a specific IP block for initial data migration later if needed,
      # but Cloud Run uses the Unix socket connection via `cloud_sql_instance` configuration.
    }
    
    backup_configuration {
      enabled    = true
      start_time = "03:00" 
    }
  }

  deletion_protection = false # Set to false to allow terraform destroy for this dev env
}

resource "google_sql_database" "database" {
  name     = "soccer_ai"
  instance = google_sql_database_instance.postgres.name
  project  = var.project_id
}

resource "google_sql_user" "users" {
  name     = "soccer_app"
  instance = google_sql_database_instance.postgres.name
  password = random_password.db_password.result
  project  = var.project_id
}

output "postgres_connection_name" {
  value = google_sql_database_instance.postgres.connection_name
}
