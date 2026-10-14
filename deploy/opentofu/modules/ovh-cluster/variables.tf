variable "project_id" {
  description = "Projet Public Cloud OVHcloud qui héberge la cellule."
  type        = string
}

variable "cell" {
  description = "Identifiant de la cellule (mut-fr-01, recette, ded-cabinet-x)."
  type        = string

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{2,30}$", var.cell))
    error_message = "Identifiant de cellule : minuscules, chiffres et tirets."
  }
}

variable "offer" {
  description = "Offre d'hébergement (annexe E §3)."
  type        = string
  default     = "mutualisee"

  validation {
    condition     = contains(["mutualisee", "dediee", "recette"], var.offer)
    error_message = "Offre : mutualisee, dediee ou recette."
  }
}

variable "size" {
  description = "Taille de la cellule (S, M, L, XL) ; reprise par le chart lettra-cell."
  type        = string
  default     = "S"

  validation {
    condition     = contains(["S", "M", "L", "XL"], var.size)
    error_message = "Taille : S, M, L ou XL."
  }
}

variable "region" {
  description = "Région OVHcloud en France uniquement (données souveraines, ADR-0011)."
  type        = string
  default     = "GRA11"

  validation {
    condition     = can(regex("^(GRA|SBG|RBX|EU-WEST-PAR)", upper(var.region)))
    error_message = "La cellule doit rester en France (GRA, SBG, RBX ou Paris)."
  }
}

variable "kubernetes_version" {
  type    = string
  default = "1.33"
}

variable "vlan_id" {
  type    = number
  default = 100
}

variable "subnet_cidr" {
  type    = string
  default = "10.42.0.0/16"
}

variable "node_pools" {
  description = "Familles de nœuds : gabarit, minimum et maximum (l'autoscaler du cluster ajuste entre les deux)."
  type = map(object({
    flavor = string
    min    = number
    max    = number
    taints = optional(list(object({ key = string, value = string, effect = string })), [])
  }))
}

variable "backup_lock_days" {
  description = "Durée du verrou d'objet des sauvegardes, en jours."
  type        = number
  default     = 35
}
