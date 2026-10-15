{{- define "lettra-observability.labels" -}}
app.kubernetes.io/part-of: lettra
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
lettra.fr/cell: {{ .Values.cell | quote }}
{{- end -}}

{{/*
Second fichier de configuration du collecteur (backends.yaml), fusionné par le collecteur après files/collector.yaml :
exportateurs des traces et des journaux activés dans values.yaml et liste des exportateurs de chaque pipeline. Ces
listes remplacent celles de la base : debug et Prometheus y sont donc repris.
*/}}
{{- define "lettra-observability.collectorBackends" -}}
{{- $c := .Values.collector -}}
{{- $f := $c.fournisseur -}}
{{- if and $f.enabled (not $f.endpoint) }}{{ fail "collector.fournisseur.endpoint est obligatoire quand le fournisseur est activé" }}{{ end -}}
{{- $traces := list "debug" -}}
{{- $metrics := list "otlphttp/prometheus" "debug" -}}
{{- $logs := list "debug" -}}
{{- if $c.traces.tempo.enabled }}{{ $traces = append $traces "otlp/tempo" }}{{ end -}}
{{- if $c.logs.loki.enabled }}{{ $logs = append $logs "otlphttp/loki" }}{{ end -}}
{{- if $f.enabled -}}
{{- if has "traces" $f.signals }}{{ $traces = append $traces "otlphttp/fournisseur" }}{{ end -}}
{{- if has "metrics" $f.signals }}{{ $metrics = append $metrics "otlphttp/fournisseur" }}{{ end -}}
{{- if has "logs" $f.signals }}{{ $logs = append $logs "otlphttp/fournisseur" }}{{ end -}}
{{- end -}}
# Rendu par le chart lettra-observability depuis values.yaml (collector.traces, collector.logs, collector.fournisseur).
{{- if or $c.traces.tempo.enabled $c.logs.loki.enabled $f.enabled }}
exporters:
{{- if $c.traces.tempo.enabled }}
  otlp/tempo:
    endpoint: {{ $c.traces.tempo.endpoint | quote }}
    tls:
      insecure: true
{{- end }}
{{- if $c.logs.loki.enabled }}
  otlphttp/loki:
    endpoint: {{ $c.logs.loki.endpoint | quote }}
    tls:
      insecure: true
{{- end }}
{{- if $f.enabled }}
  otlphttp/fournisseur:
    endpoint: {{ $f.endpoint | quote }}
    {{- if $f.authSecret }}
    headers:
      authorization: ${env:FOURNISSEUR_AUTORISATION}
    {{- end }}
{{- end }}
{{- end }}
service:
  pipelines:
    traces:
      exporters: {{ toJson $traces }}
    metrics:
      exporters: {{ toJson $metrics }}
    logs:
      exporters: {{ toJson $logs }}
{{- end -}}
