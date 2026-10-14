{{- define "lettra-api.name" -}}{{ .Release.Name }}-api{{- end -}}

{{- define "lettra-api.selector" -}}
app.kubernetes.io/name: lettra-api
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "lettra-api.labels" -}}
{{ include "lettra-api.selector" . }}
app.kubernetes.io/part-of: lettra
app.kubernetes.io/version: {{ .Values.image.tag | default .Chart.AppVersion | quote }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
lettra.fr/cell: {{ .Values.cell | quote }}
{{- end -}}
