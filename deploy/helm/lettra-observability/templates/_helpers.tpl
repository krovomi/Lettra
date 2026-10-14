{{- define "lettra-observability.labels" -}}
app.kubernetes.io/part-of: lettra
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
lettra.fr/cell: {{ .Values.cell | quote }}
{{- end -}}
