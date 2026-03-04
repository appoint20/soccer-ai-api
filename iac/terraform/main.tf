terraform {
  required_providers {
    google = {
      source  = "hashicorp/google"
      version = ">= 5.45.0"
    }
  }

  # We will use local state for simplicity instead of requiring a pre-existing gs bucket
}

provider "google" {
  project = var.project_id
  region  = var.region
}

# Enable required Google Cloud APIs
resource "google_project_service" "required_apis" {
  for_each = toset([
    "run.googleapis.com",             # Cloud Run
    "secretmanager.googleapis.com",   # Secret Manager
    "cloudscheduler.googleapis.com",  # Cloud Scheduler
    "iam.googleapis.com",             # IAM APIs
    "aiplatform.googleapis.com"       # Vertex AI (for Gemini)
  ])

  project = var.project_id
  service = each.key

  disable_on_destroy = false
}
