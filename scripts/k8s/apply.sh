#!/usr/bin/env bash
set -euo pipefail

# The database-then-migrate-then-everything-else ordering this script used
# to spell out by hand now lives in the chart itself, as Helm pre-install/
# pre-upgrade hooks (infra/helm/gaming-backend-platform/templates/
# statefulset.yaml and migration-job.yaml) -- a Job has no depends_on
# equivalent, but Helm's hook mechanism is exactly what exists to express
# that ordering declaratively instead of a wrapper script re-implementing
# kubectl wait by hand. This script's only remaining job is the one thing
# Helm can't read on its own: ocelot.Kubernetes.json lives under
# backend/ApiGateway/, not under the chart, so it goes in via --set-file
# rather than a second copy drifting inside infra/helm/.

NAMESPACE="gaming-platform"
CHART="infra/helm/gaming-backend-platform"
RELEASE="gbp"

helm upgrade --install "$RELEASE" "$CHART" \
  --namespace "$NAMESPACE" --create-namespace \
  -f "$CHART/values-local.yaml" \
  --set-file gateway.ocelotConfigJson=backend/ApiGateway/ocelot.Kubernetes.json
