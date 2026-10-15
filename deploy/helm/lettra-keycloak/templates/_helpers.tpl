{{- define "lettra-keycloak.selector" -}}
app.kubernetes.io/name: keycloak
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "lettra-keycloak.labels" -}}
{{ include "lettra-keycloak.selector" . }}
app.kubernetes.io/part-of: lettra
app.kubernetes.io/version: {{ .Values.image.tag | quote }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
lettra.fr/cell: {{ .Values.cell | quote }}
{{- end -}}

{{/* Hôte de la base : service rw du cluster CloudNativePG, ou base externe. */}}
{{- define "lettra-keycloak.dbHost" -}}
{{- if .Values.database.cnpg.enabled -}}{{ .Values.database.cnpg.name }}-rw{{- else -}}{{ .Values.database.host }}{{- end -}}
{{- end -}}

{{- define "lettra-keycloak.dbSecret" -}}
{{- if .Values.database.cnpg.enabled -}}{{ .Values.database.cnpg.name }}-app{{- else -}}{{ .Values.database.existingSecret }}{{- end -}}
{{- end -}}

{{- define "lettra-keycloak.securityContext" -}}
allowPrivilegeEscalation: false
capabilities: { drop: [ALL] }
{{- end -}}
