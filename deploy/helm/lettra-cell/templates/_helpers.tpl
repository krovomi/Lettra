{{- define "lettra-cell.labels" -}}
app.kubernetes.io/part-of: lettra
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
lettra.fr/cell: {{ .Values.cell | quote }}
{{- end -}}

{{/* Connexions serveur par instance de PgBouncer : le budget du cluster réparti sur les instances, arrondi au-dessus. */}}
{{- define "lettra-cell.perPoolerBudget" -}}
{{- $budget := int .cluster.activeDossierBudget -}}
{{- $instances := int .root.Values.postgres.pooler.instances -}}
{{- div (add $budget (sub $instances 1)) $instances -}}
{{- end -}}
