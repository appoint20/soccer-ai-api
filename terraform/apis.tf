resource "google_project_service" "required_apis" {
  for_each = toset([
    "artifactregistry.googleapis.com",
    "secretmanager.googleapis.com",
    "run.googleapis.com",
    "iam.googleapis.com"
  ])

  project = var.project_id
  service = each.key

  # Do not disable the API if you delete the terraform resource
  disable_on_destroy = false
}
