variable "project_id" {
  description = "The GCP Project ID"
  type        = string
}

variable "region" {
  description = "The GCP region for resources"
  type        = string
  default     = "europe-west3"
}

variable "docker_image_api" {
  description = "The Docker image for the .NET API"
  type        = string
}


