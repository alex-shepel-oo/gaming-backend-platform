#!/usr/bin/env bash
set -euo pipefail

# Argo CD is a cluster addon, not something this project's own Helm chart
# deploys, the same relationship install-traefik.sh has to the Traefik
# release, just pointed at Argo CD's own official chart instead. Run this
# after install-traefik.sh (argocd-values-local.yaml routes the UI through
# it at argocd.localhost, the same *.localhost convention every other local
# UI in this project already follows).

helm repo add argo https://argoproj.github.io/argo-helm >/dev/null
helm repo update argo >/dev/null

helm upgrade --install argocd argo/argo-cd \
  --namespace argocd --create-namespace \
  -f "$(dirname "$0")/argocd-values-local.yaml" \
  --wait --timeout 300s
