# Cellule de recette (phase 2) : recréée de zéro par la pipeline (annexe C, sortie de phase 2).
#   tofu init -backend-config=backend.hcl && tofu apply
# Identifiants attendus dans l'environnement : OVH_ENDPOINT, OVH_APPLICATION_KEY, OVH_APPLICATION_SECRET,
# OVH_CONSUMER_KEY, AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY (état distant), TF_VAR_state_passphrase.

terraform {
  required_version = ">= 1.10"

  # État dans le stockage objet OVHcloud (compatible S3), en France ; complété par backend.hcl.
  backend "s3" {
    key                         = "cellules/recette.tfstate"
    region                      = "gra"
    skip_credentials_validation = true
    skip_region_validation      = true
    skip_requesting_account_id  = true
    skip_s3_checksum            = true
    use_path_style              = true
    use_lockfile                = true
  }

  required_providers {
    ovh        = { source = "ovh/ovh", version = "~> 2.22" }
    kubernetes = { source = "hashicorp/kubernetes", version = "~> 3.3" }
    helm       = { source = "hashicorp/helm", version = "~> 3.3" }
  }
}

provider "ovh" {}

module "cluster" {
  source = "../../modules/ovh-cluster"

  project_id = var.ovh_project_id
  cell       = "recette"
  offer      = "recette"
  size       = "S"
  region     = "GRA11"
  node_pools = {
    systeme      = { flavor = "b3-8", min = 1, max = 2 }
    applications = { flavor = "b3-16", min = 2, max = 4 }
    bases = {
      flavor = "r3-32", min = 2, max = 2
      taints = [{ key = "lettra.fr/bases", value = "true", effect = "NoSchedule" }]
    }
  }
}

locals {
  kube = module.cluster.kubeconfig
}

provider "kubernetes" {
  host                   = local.kube.host
  cluster_ca_certificate = base64decode(local.kube.cluster_ca_certificate)
  client_certificate     = base64decode(local.kube.client_certificate)
  client_key             = base64decode(local.kube.client_key)
}

provider "helm" {
  kubernetes = {
    host                   = local.kube.host
    cluster_ca_certificate = base64decode(local.kube.cluster_ca_certificate)
    client_certificate     = base64decode(local.kube.client_certificate)
    client_key             = base64decode(local.kube.client_key)
  }
}

module "bootstrap" {
  source = "../../modules/cell-bootstrap"

  cell                = "recette"
  size                = "S"
  git_revision        = var.git_revision
  image_tag           = var.image_tag
  alerting            = var.alerting
  bff_client_secret   = var.bff_client_secret
  storage_credentials = module.cluster.storage_credentials
}
