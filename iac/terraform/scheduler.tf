resource "google_project_iam_member" "scheduler_run_invoker" {
  project = var.project_id
  role    = "roles/run.invoker"
  member  = "serviceAccount:appoint@soccer-ai-250226.iam.gserviceaccount.com"
}

resource "google_cloud_scheduler_job" "daily_sync_scheduler" {
  name             = "trigger-soccer-ai-sync"
  description      = "Triggers the Soccer AI Daily Sync API every day at 02:00 AM"
  schedule         = "0 2 * * *"
  time_zone        = "Europe/Berlin"
  region           = var.region
  project          = var.project_id

  http_target {
    http_method = "POST"
    uri         = "${google_cloud_run_v2_service.api_service.uri}/api/automation/sync-daily"

    headers = {
      "Content-Type" = "application/json"
      "X-API-Key"    = "700c846f-cdf8-416b-aea7-6b0b4892aa5d"
    }

    oidc_token {
      service_account_email = "appoint@soccer-ai-250226.iam.gserviceaccount.com"
      audience              = google_cloud_run_v2_service.api_service.uri
    }
  }
}
