output "namespaces" {
  value = [for ns in kubernetes_namespace_v1.cell : ns.metadata[0].name]
}
