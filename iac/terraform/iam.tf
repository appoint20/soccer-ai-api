# ----------------------------------------------------------------------------------
# Permissions for the EXISTING Service Account (Appoint)
# ----------------------------------------------------------------------------------

# Grant the Appoint Service Account permission to Invoke the Cloud Run API
# (Needed for Cloud Scheduler to trigger the API)
resource "google_project_iam_member" "appoint_api_invoker" {
  project = var.project_id
  role    = "roles/run.invoker"
  member  = "serviceAccount:appoint@soccer-ai-250226.iam.gserviceaccount.com"
}

# Grant Secret Manager Access
resource "google_project_iam_member" "appoint_secret_accessor" {
  project = var.project_id
  role    = "roles/secretmanager.secretAccessor"
  member  = "serviceAccount:appoint@soccer-ai-250226.iam.gserviceaccount.com"
}

# Grant Cloud Storage access for SQLite GCS FUSE mapping
resource "google_storage_bucket_iam_member" "appoint_storage_admin" {
  bucket = data.google_storage_bucket.database_bucket.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:appoint@soccer-ai-250226.iam.gserviceaccount.com"
}
