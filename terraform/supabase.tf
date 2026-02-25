# Note: Supabase free tier projects are often easily created via the dashboard.
# This template uses the community provider for reference.

/*
terraform {
  required_providers {
    supabase = {
      source  = "supabase/supabase"
      version = "~> 1.0"
    }
  }
}

provider "supabase" {
  access_token = var.supabase_access_token
}

resource "supabase_project" "soccer_ai" {
  organization_id = var.supabase_org_id
  name            = "soccer-ai"
  database_password = var.supabase_db_password
  region          = "eu-central-1"
}
*/

# For the free version, manually provisioned connection strings should be 
# added to the GCP Secret Manager secret: POSTGRES_CONNECTION_STRING
