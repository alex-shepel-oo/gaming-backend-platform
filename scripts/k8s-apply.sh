#!/usr/bin/env bash
set -euo pipefail

# gateway/kustomization.yaml generates gateway-config straight from
# backend/ApiGateway/ocelot.Kubernetes.json, which sits outside the
# infra/kubernetes/gateway/ tree. Kustomize's default LoadRestrictionsRootOnly
# refuses to read it, and `kubectl apply -k` has no flag to relax that --
# only the separate `kubectl kustomize` render subcommand does. So applying
# this tree is a render-then-apply pipe, not a bare `apply -k`; this script
# is the one place that pipe is spelled out, so the flag can't get dropped
# by hand next time.

TARGET="${1:-infra/kubernetes}"
NAMESPACE="gaming-platform"

# Kustomize has no notion of apply order, and there's no Helm here to hook
# into -- so for a full-tree apply, the two database tiers and their
# migration Jobs are applied and waited on explicitly, in this one script,
# before identity-service/economy-service exist to take traffic. A narrower
# TARGET (e.g. this script pointed at just infra/kubernetes/gateway) skips
# straight to the render-and-apply below; migrations aren't relevant there.
if [ "$TARGET" = "infra/kubernetes" ]; then
  kubectl apply -f "$TARGET/base/"
  kubectl apply -f "$TARGET/identity/configmap.yaml" -f "$TARGET/identity/db-statefulset.yaml" -f "$TARGET/identity/db-service.yaml"
  kubectl apply -f "$TARGET/economy/configmap.yaml" -f "$TARGET/economy/db-statefulset.yaml" -f "$TARGET/economy/db-service.yaml"

  kubectl -n "$NAMESPACE" rollout status statefulset/identity-db --timeout=180s
  kubectl -n "$NAMESPACE" rollout status statefulset/economy-db --timeout=180s

  # Same ordering docker-compose gets for free from identity-migrator's/
  # economy-migrator's depends_on: condition: service_completed_successfully
  # -- spelled out here since a Job has no equivalent of that.
  kubectl apply -f "$TARGET/identity/migration-job.yaml"
  kubectl apply -f "$TARGET/economy/migration-job.yaml"

  kubectl -n "$NAMESPACE" wait --for=condition=complete job/identity-migrator --timeout=120s
  kubectl -n "$NAMESPACE" wait --for=condition=complete job/economy-migrator --timeout=120s
fi

kubectl kustomize --load-restrictor=LoadRestrictionsNone "$TARGET" | kubectl apply -f -
