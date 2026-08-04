{{/*
Common labels applied to every object this chart renders.
*/}}
{{- define "gbp.labels" -}}
app.kubernetes.io/part-of: gaming-backend-platform
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{- end -}}

