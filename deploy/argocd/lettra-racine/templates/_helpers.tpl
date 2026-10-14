{{- define "racine.app" -}}
apiVersion: argoproj.io/v1alpha1
kind: Application
metadata:
  name: {{ .root.Values.cell }}-{{ .name }}
  namespace: argocd
  labels:
    lettra.fr/cell: {{ .root.Values.cell | quote }}
  annotations:
    argocd.argoproj.io/sync-wave: {{ .wave | quote }}
  finalizers:
    - resources-finalizer.argocd.argoproj.io
spec:
  project: {{ .root.Values.project }}
  destination:
    server: https://kubernetes.default.svc
    namespace: {{ .namespace }}
  syncPolicy:
    {{- toYaml .root.Values.syncPolicy | nindent 4 }}
{{- end -}}

{{/* Fichiers de valeurs : communs, puis propres à la cellule (le second l'emporte). */}}
{{- define "racine.values" -}}
- $lettra/deploy/cells/_commun/{{ .file }}
- $lettra/deploy/cells/{{ .root.Values.cell }}/{{ .file }}
{{- end -}}
