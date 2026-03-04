# The public API Web Service
resource "google_cloud_run_v2_service" "api_service" {
  name     = "soccer-ai-api"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"

  deletion_protection = false

  template {
    scaling {
      max_instance_count = 5
    }
    containers {
      image = var.docker_image_api

      env {
        name = "APIFOOTBALL_API_KEY"
        value_source {
          secret_key_ref {
            secret  = "APIFOOTBALL_API_KEY"
            version = "latest"
          }
        }
      }

      env {
        name = "GEMINI_API_KEY"
        value_source {
          secret_key_ref {
            secret  = "GEMINI_API_KEY"
            version = "latest"
          }
        }
      }

      env {
        name = "ConnectionStrings__PostgresConnection"
        value_source {
          secret_key_ref {
            secret  = "POSTGRES_CONNECTION_STRING"
            version = "latest"
          }
        }
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name = "Jwt__Secret"
        value_source {
          secret_key_ref {
            secret  = "JWT_SECRET"
            version = "latest"
          }
        }
      }

      # Provide access to the persistent SQLite Database
      # NOTE: For a production Cloud Run environment handling 25,000 users, 
      # Cloud SQL (PostgreSQL) is the recommended backend since Cloud Run containers are ephemeral
      # and do not share local disk state. As this architecture is built on SQLite, we mount a Cloud Storage FUSE bucket.
      resources {
        limits = {
          cpu    = "1000m"
          memory = "2Gi"
        }
      }

      volume_mounts {
        name       = "cloudsql"
        mount_path = "/cloudsql"
      }
      startup_probe {
        initial_delay_seconds = 10
        timeout_seconds       = 240
        period_seconds        = 240
        failure_threshold     = 3
        tcp_socket {
          port = 8080
        }
      }
    }

    volumes {
      name = "cloudsql"
      cloud_sql_instance {
        instances = [google_sql_database_instance.postgres.connection_name]
      }
    }

    service_account = "appoint@soccer-ai-250226.iam.gserviceaccount.com"
    
    timeout = "600s"
    execution_environment = "EXECUTION_ENVIRONMENT_GEN2"
  }

  depends_on = [google_project_service.required_apis]
}

# Allow unauthenticated invocations for the public API
resource "google_cloud_run_service_iam_member" "api_public_access" {
  location = google_cloud_run_v2_service.api_service.location
  project  = google_cloud_run_v2_service.api_service.project
  service  = google_cloud_run_v2_service.api_service.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

