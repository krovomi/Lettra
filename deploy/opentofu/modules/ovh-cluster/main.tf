# Socle d'une cellule chez OVHcloud, en France (annexe E §4, ADR-0009) : réseau privé, Kubernetes managé sur trois
# familles de nœuds, buckets de la cellule (sauvegardes en verrou d'objet, journaux). Les bases de dossiers ne sont pas
# décrites ici : CloudNativePG les héberge dans le cluster et le Worker les crée à la demande.

terraform {
  required_version = ">= 1.10"
  required_providers {
    ovh = {
      source  = "ovh/ovh"
      version = "~> 2.22"
    }
  }
}

locals {
  prefix = "lettra-${var.cell}"
  labels = {
    "lettra.fr/cell"   = var.cell
    "lettra.fr/offre"  = var.offer
    "lettra.fr/taille" = var.size
  }
}

resource "ovh_cloud_project_network_private" "cell" {
  service_name = var.project_id
  name         = "${local.prefix}-reseau"
  regions      = [var.region]
  vlan_id      = var.vlan_id
}

resource "ovh_cloud_project_network_private_subnet" "cell" {
  service_name = var.project_id
  network_id   = ovh_cloud_project_network_private.cell.id
  region       = var.region
  network      = var.subnet_cidr
  start        = cidrhost(var.subnet_cidr, 10)
  end          = cidrhost(var.subnet_cidr, 250)
  dhcp         = true
  no_gateway   = false
}

resource "ovh_cloud_project_kube" "cell" {
  service_name       = var.project_id
  name               = local.prefix
  region             = var.region
  version            = var.kubernetes_version
  private_network_id = tolist(ovh_cloud_project_network_private.cell.regions_attributes)[0].openstackid

  private_network_configuration {
    default_vrack_gateway              = ""
    private_network_routing_as_default = false
  }

  depends_on = [ovh_cloud_project_network_private_subnet.cell]
}

# Trois familles de nœuds (annexe E §5) : système (Argo CD, observabilité), applications (API, Worker), bases.
resource "ovh_cloud_project_kube_nodepool" "pools" {
  for_each = var.node_pools

  service_name   = var.project_id
  kube_id        = ovh_cloud_project_kube.cell.id
  name           = each.key
  flavor_name    = each.value.flavor
  desired_nodes  = each.value.min
  min_nodes      = each.value.min
  max_nodes      = each.value.max
  autoscale      = each.value.min != each.value.max
  monthly_billed = false

  template {
    metadata {
      labels      = merge(local.labels, { "lettra.fr/pool" = each.key })
      annotations = {}
      finalizers  = []
    }
    spec {
      unschedulable = false
      taints        = each.value.taints
    }
  }
}

# Sauvegardes PostgreSQL (WAL et sauvegardes quotidiennes) : verrou d'objet, conformité, hors de la cellule.
resource "ovh_cloud_project_storage" "backups" {
  service_name = var.project_id
  region_name  = upper(var.region)
  name         = "${local.prefix}-sauvegardes"

  versioning = {
    status = "enabled"
  }

  object_lock = {
    status = "enabled"
    rule = {
      mode   = "compliance"
      period = "P${var.backup_lock_days}D"
    }
  }
}

resource "ovh_cloud_project_storage" "logs" {
  service_name = var.project_id
  region_name  = upper(var.region)
  name         = "${local.prefix}-journaux"
}

# Compte technique de la cellule pour le stockage objet (CloudNativePG, Loki) ; ses clés partent dans le coffre.
resource "ovh_cloud_project_user" "storage" {
  service_name = var.project_id
  description  = "${local.prefix} stockage objet"
  role_names   = ["objectstore_operator"]
}

resource "ovh_cloud_project_user_s3_credential" "storage" {
  service_name = var.project_id
  user_id      = ovh_cloud_project_user.storage.id
}
