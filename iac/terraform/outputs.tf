output "api_url" {
  description = "The public URL of the deployed API"
  value       = google_cloud_run_v2_service.api_service.uri
}
