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

kubectl kustomize --load-restrictor=LoadRestrictionsNone "$TARGET" | kubectl apply -f -
