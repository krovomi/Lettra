variable "cell" {
  type = string
}

variable "size" {
  type    = string
  default = "S"
}

variable "git_revision" {
  description = "Branche ou étiquette suivie par Argo CD."
  type        = string
  default     = "main"
}

variable "image_tag" {
  description = "SHA de l'image Lettra signée par la CI."
  type        = string
}

variable "argocd_chart_version" {
  type    = string
  default = "10.10.3"
}

variable "wait_for_workloads" {
  description = "Attendre que les pods d'Argo CD soient prêts (faux pour une validation sans nœud capable de les exécuter)."
  type        = bool
  default     = true
}

variable "alerting" {
  description = "Adresses de l'astreinte et jeton du webhook Supervision (annexe G §4)."
  type = object({
    heartbeat_url     = string
    supervision_url   = string
    supervision_token = string
    oncall_page_url   = string
    oncall_ticket_url = string
  })
  sensitive = true
}

variable "storage_credentials" {
  description = "Clés S3 des sauvegardes (null en local)."
  type = object({
    access_key_id     = string
    secret_access_key = string
  })
  default   = null
  sensitive = true
}
