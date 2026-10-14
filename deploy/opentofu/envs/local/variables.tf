variable "kubeconfig" {
  type    = string
  default = "~/.kube/config"
}

variable "kube_context" {
  type    = string
  default = null
}

variable "cell" {
  type    = string
  default = "locale"
}

variable "git_revision" {
  type    = string
  default = "main"
}

variable "image_tag" {
  type = string
}

variable "wait_for_workloads" {
  type    = bool
  default = true
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
  default = {
    heartbeat_url     = "http://astreinte-factice.lettra-observabilite/battement"
    supervision_url   = "http://lettra-api.lettra/internal/supervision/alertmanager"
    supervision_token = "jeton-local-non-secret"
    oncall_page_url   = "http://astreinte-factice.lettra-observabilite/appel"
    oncall_ticket_url = "http://astreinte-factice.lettra-observabilite/ticket"
  }
}
