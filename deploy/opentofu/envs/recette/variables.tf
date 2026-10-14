variable "ovh_project_id" {
  type = string
}

variable "git_revision" {
  type    = string
  default = "main"
}

variable "image_tag" {
  type = string
}

variable "alerting" {
  type = object({
    heartbeat_url     = string
    supervision_url   = string
    supervision_token = string
    oncall_page_url   = string
    oncall_ticket_url = string
  })
  sensitive = true
}

variable "bff_client_secret" {
  description = "Secret du client BFF du realm de recette (coffre de la pipeline)."
  type        = string
  default     = null
  sensitive   = true
}

variable "state_passphrase" {
  description = "Phrase de passe du chiffrement de l'état (coffre de la pipeline), au moins 16 caractères."
  type        = string
  sensitive   = true
}
