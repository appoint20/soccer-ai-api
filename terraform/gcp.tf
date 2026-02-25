# Artifact Registry for Docker images
resource "google_artifact_registry_repository" "soccer_ai_repo" {
  location      = var.region
  repository_id = var.repository_id
  description   = "Docker repository for Soccer AI API"
  format        = "DOCKER"
}

# Secret Manager for Connection Strings and API Keys
resource "google_secret_manager_secret" "postgres_connection_string" {
  secret_id = "POSTGRES_CONNECTION_STRING"
  replication {
    user_managed {
      replicas {
        location = var.region
      }
    }
  }
}

resource "google_secret_manager_secret" "api_football_key" {
  secret_id = "APIFOOTBALL_API_KEY"
  replication {
    user_managed {
      replicas {
        location = var.region
      }
    }
  }
}

resource "google_secret_manager_secret" "gemini_api_key" {
  secret_id = "GEMINI_API_KEY"
  replication {
    user_managed {
      replicas {
        location = var.region
      }
    }
  }
}
