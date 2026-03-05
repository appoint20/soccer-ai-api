data "google_storage_bucket" "database_bucket" {
  name = "soccer-ai-db-${var.project_id}"
}

output "database_bucket_name" {
  value = data.google_storage_bucket.database_bucket.name
}
