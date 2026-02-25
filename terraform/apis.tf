resource "google_project_service" "required_apis" {
  for_each = toset([
    "secretmanager.googleapis.com",
    "artifactregistry.googleapis.com",
    "compute.googleapis.com",
    "iam.googleapis.com"
  ])

  project            = var.project_id
  service            = each.key
  disable_on_destroy = false
}
