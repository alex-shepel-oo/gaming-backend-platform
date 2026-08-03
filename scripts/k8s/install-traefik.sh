#!/usr/bin/env bash
set -euo pipefail

# Traefik is a cluster addon, not something the app's own Helm chart deploys
# -- same relationship the old Kustomize tree had with ingress-nginx, just a
# separate release in its own namespace. Run this once per cluster (kind's
# own node image ships no ingress controller by default) before
# scripts/k8s/apply.sh. kind-config.yaml maps host ports 80/443 onto the
# node container; traefik-values-local.yaml binds Traefik's pod to those
# same two ports via hostPort, closing the loop.

helm repo add traefik https://traefik.github.io/charts >/dev/null
helm repo update traefik >/dev/null

helm upgrade --install traefik traefik/traefik \
  --namespace traefik --create-namespace \
  -f "$(dirname "$0")/traefik-values-local.yaml" \
  --wait --timeout 180s
