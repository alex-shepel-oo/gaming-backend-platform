#!/usr/bin/env bash
set -euo pipefail

# node-exporter is a cluster addon, not something the app's own Helm chart
# deploys -- same relationship install-traefik.sh/install-argocd.sh have to
# their own releases. Gives Prometheus (infra/prometheus/prometheus.yml's
# node-exporter job) the host-level CPU/memory/disk numbers for the node
# itself, which nothing else in this stack exposes. Same for both local and
# production clusters, no separate values file needed.

helm repo add prometheus-community https://prometheus-community.github.io/helm-charts >/dev/null
helm repo update prometheus-community >/dev/null

helm upgrade --install node-exporter prometheus-community/prometheus-node-exporter \
  --namespace observability --create-namespace \
  --set fullnameOverride=node-exporter \
  --wait --timeout 180s
