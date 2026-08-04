#!/usr/bin/env bash
set -euo pipefail

# kube-state-metrics is a cluster addon, same relationship as node-exporter
# (see install-node-exporter.sh) -- gives Prometheus (infra/prometheus/
# prometheus.yml's kube-state-metrics job) Kubernetes object state (pod
# phase, restart counts, deployment replica availability), which is not the
# same thing cAdvisor's per-container CPU/memory reports. Same for both
# local and production clusters, no separate values file needed.

helm repo add prometheus-community https://prometheus-community.github.io/helm-charts >/dev/null
helm repo update prometheus-community >/dev/null

helm upgrade --install kube-state-metrics prometheus-community/kube-state-metrics \
  --namespace observability --create-namespace \
  --set fullnameOverride=kube-state-metrics \
  --wait --timeout 180s
