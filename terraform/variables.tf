variable "project_id" {
  description = "The GCP Project ID"
  type        = string
}

variable "region" {
  description = "The GCP region for resources"
  type        = string
  default     = "europe-west3"
}

variable "repository_id" {
  description = "The ID of the Artifact Registry repository"
  type        = string
  default     = "soccer-ai"
}

variable "supabase_access_token" {
  description = "Supabase Access Token"
  type        = string
  sensitive   = true
}

variable "supabase_org_id" {
  description = "Supabase Organization ID"
  type        = string
}

variable "supabase_db_password" {
  description = "Supabase Database Password"
  type        = string
  sensitive   = true
}
