# Cellule sur un Kubernetes local (k3s, kind) ou sur site (même recette que le SaaS, annexe E §3).
#   tofu init && tofu apply -var image_tag=<sha> -var-file=local.tfvars

terraform {
  required_version = ">= 1.10"
  required_providers {
    kubernetes = { source = "hashicorp/kubernetes", version = "~> 3.3" }
    helm       = { source = "hashicorp/helm", version = "~> 3.3" }
  }
}

provider "kubernetes" {
  config_path    = var.kubeconfig
  config_context = var.kube_context
}

provider "helm" {
  kubernetes = {
    config_path    = var.kubeconfig
    config_context = var.kube_context
  }
}

module "bootstrap" {
  source = "../../modules/cell-bootstrap"

  cell               = var.cell
  size               = "S"
  git_revision       = var.git_revision
  image_tag          = var.image_tag
  wait_for_workloads = var.wait_for_workloads
  alerting           = var.alerting
}
