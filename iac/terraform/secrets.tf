resource "google_secret_manager_secret" "apifootball_api_key" {
  secret_id = "APIFOOTBALL_API_KEY"
  replication {
    auto {}
  }
}

resource "google_secret_manager_secret" "gemini_api_key" {
  secret_id = "GEMINI_API_KEY"
  replication {
    auto {}
  }
}

resource "google_secret_manager_secret" "jwt_secret" {
  secret_id = "JWT_SECRET"
  replication {
    auto {}
  }
}

resource "google_secret_manager_secret" "postgres_connection_string" {
  secret_id = "POSTGRES_CONNECTION_STRING"
  replication {
    auto {}
  }
}
