#!/usr/bin/env bash
set -euo pipefail

# The database-then-migrate-then-everything-else ordering this script used
# to spell out by hand now lives in the chart itself, as Helm pre-install/
# pre-upgrade hooks (infra/helm/gaming-backend-platform/templates/
# statefulset.yaml and migration-job.yaml) -- a Job has no depends_on
# equivalent, but Helm's hook mechanism is exactly what exists to express
# that ordering declaratively instead of a wrapper script re-implementing
# kubectl wait by hand. This script's only remaining job is the things Helm
# can't read on its own: ocelot.Kubernetes.json lives under backend/
# ApiGateway/, and the observability stack's own config files live under
# their own infra/ directories (shared with docker-compose) -- neither is a
# second copy kept inside the chart, both go in via --set-file instead.

NAMESPACE="gaming-platform"
CHART="infra/helm/gaming-backend-platform"
RELEASE="gbp"

# Per-service base spec (see ADR 0021) -- globbed rather than named one by
# one, so a new file under values/ is picked up automatically instead of
# needing this script edited every time a service is added.
VALUES_ARGS=()
for f in "$CHART"/values/*.yaml; do
  VALUES_ARGS+=(-f "$f")
done
VALUES_ARGS+=(-f "$CHART/values-local.yaml")

helm upgrade --install "$RELEASE" "$CHART" \
  --namespace "$NAMESPACE" --create-namespace \
  "${VALUES_ARGS[@]}" \
  --set-file gateway.ocelotConfigJson=backend/ApiGateway/ocelot.Kubernetes.json \
  --set-file observability.otelCollectorConfigYaml=infra/otel-collector/otel-collector-config.yaml \
  --set-file observability.tempoConfigYaml=infra/tempo/tempo-config.yaml \
  --set-file observability.prometheusConfigYaml=infra/prometheus/prometheus.yml \
  --set-file observability.lokiConfigYaml=infra/loki/loki-config.yaml \
  --set-file observability.grafanaDatasourcesYaml=infra/grafana/provisioning/datasources/datasources.yml \
  --set-file observability.grafanaDashboardsProviderYaml=infra/grafana/provisioning/dashboards/dashboards.yml \
  --set-file observability.grafanaDashboardJson=infra/grafana/dashboards/service-overview.json \
  --set-file observability.grafanaNodeResourcesDashboardJson=infra/grafana/dashboards/node-resources.json \
  --set-file emailService.templates.emailVerificationHtml=backend/EmailService/Templates/EmailVerification.html \
  --set-file emailService.templates.passwordResetHtml=backend/EmailService/Templates/PasswordReset.html \
  --set-file emailService.templates.duplicateRegistrationNoticeHtml=backend/EmailService/Templates/DuplicateRegistrationNotice.html
