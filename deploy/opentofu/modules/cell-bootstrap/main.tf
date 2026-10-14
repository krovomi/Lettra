# Amorçage GitOps d'une cellule (ADR-0009) : espaces de noms, secrets d'amorçage, Argo CD, puis l'application racine
# (deploy/argocd/lettra-racine). Ensuite, Argo CD tient la cellule à l'état décrit dans Git ; ce module ne déploie
# aucune application Lettra lui-même. Le même module sert pour une cellule OVHcloud et pour un cluster local (k3s).

terraform {
  required_version = ">= 1.10"
  required_providers {
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "~> 3.3"
    }
    helm = {
      source  = "hashicorp/helm"
      version = "~> 3.3"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.9"
    }
  }
}

locals {
  namespaces = ["argocd", "lettra", "lettra-observabilite", "cnpg-system"]
}

resource "kubernetes_namespace_v1" "cell" {
  for_each = toset(local.namespaces)

  metadata {
    name = each.key
    labels = {
      "lettra.fr/cell"                     = var.cell
      "pod-security.kubernetes.io/enforce" = each.key == "lettra" ? "restricted" : "baseline"
    }
  }
}

# Secrets d'amorçage. En production, l'opérateur External Secrets les reprendra depuis le coffre de l'hébergeur ;
# l'état OpenTofu qui les contient est chiffré (voir envs/*/encryption.tf).
resource "kubernetes_secret_v1" "alerting" {
  metadata {
    name      = "lettra-alerting"
    namespace = kubernetes_namespace_v1.cell["lettra-observabilite"].metadata[0].name
  }
  data = {
    "battement-url"        = var.alerting.heartbeat_url
    "supervision-url"      = var.alerting.supervision_url
    "supervision-token"    = var.alerting.supervision_token
    "astreinte-appel-url"  = var.alerting.oncall_page_url
    "astreinte-ticket-url" = var.alerting.oncall_ticket_url
  }
}

resource "kubernetes_secret_v1" "api" {
  metadata {
    name      = "lettra-api"
    namespace = kubernetes_namespace_v1.cell["lettra"].metadata[0].name
  }
  data = merge(
    { "Lettra__Supervision__WebhookToken" = var.alerting.supervision_token },
    var.bff_client_secret == null ? {} : { "Lettra__Identity__Oidc__BffClientSecret" = var.bff_client_secret },
  )
}

# Redis de la cellule : cache distribué, sessions du BFF et trousseau Data Protection de l'API.
resource "random_password" "redis" {
  length  = 40
  special = false
}

resource "kubernetes_secret_v1" "redis" {
  metadata {
    name      = "lettra-redis"
    namespace = kubernetes_namespace_v1.cell["lettra"].metadata[0].name
  }
  data = {
    password            = random_password.redis.result
    "connection-string" = "redis:6379,password=${random_password.redis.result},abortConnect=false"
  }
}

resource "kubernetes_secret_v1" "backups" {
  count = var.storage_credentials == null ? 0 : 1

  metadata {
    name      = "lettra-sauvegardes"
    namespace = kubernetes_namespace_v1.cell["lettra"].metadata[0].name
  }
  data = {
    ACCESS_KEY_ID     = var.storage_credentials.access_key_id
    ACCESS_SECRET_KEY = var.storage_credentials.secret_access_key
  }
}

resource "helm_release" "argocd" {
  name       = "argocd"
  namespace  = kubernetes_namespace_v1.cell["argocd"].metadata[0].name
  repository = "https://argoproj.github.io/argo-helm"
  chart      = "argo-cd"
  version    = var.argocd_chart_version
  wait       = var.wait_for_workloads

  values = [yamlencode({
    configs = {
      params = { "server.insecure" = false }
      cm     = { "admin.enabled" = false } # Connexion par Keycloak (chantier Identité), jamais par le compte admin.
    }
    dex = { enabled = false }
  })]
}

resource "helm_release" "root" {
  name      = "lettra-racine"
  namespace = kubernetes_namespace_v1.cell["argocd"].metadata[0].name
  chart     = "${path.module}/../../../argocd/lettra-racine"
  wait      = false

  values = [yamlencode({
    cell     = var.cell
    size     = var.size
    revision = var.git_revision
    imageTag = var.image_tag
  })]

  depends_on = [helm_release.argocd]
}
