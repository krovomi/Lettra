output "kubeconfig" {
  description = "Accès administrateur au cluster, transmis au module cell-bootstrap (jamais écrit sur disque)."
  value       = ovh_cloud_project_kube.cell.kubeconfig_attributes[0]
  sensitive   = true
}

output "backups_bucket" {
  value = ovh_cloud_project_storage.backups.name
}

output "logs_bucket" {
  value = ovh_cloud_project_storage.logs.name
}

output "storage_credentials" {
  description = "Clés S3 du compte technique, à verser au coffre de la cellule."
  value = {
    access_key_id     = ovh_cloud_project_user_s3_credential.storage.access_key_id
    secret_access_key = ovh_cloud_project_user_s3_credential.storage.secret_access_key
  }
  sensitive = true
}
