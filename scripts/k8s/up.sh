#!/usr/bin/env bash
set -euo pipefail

# One command from nothing to a working local Kubernetes deployment: create
# the kind cluster if it doesn't exist yet, install Traefik as its ingress
# controller, generate and apply real (but local-only) secrets the first
# time this runs, never overwriting ones already applied, the same idempotent
# philosophy as scripts/all/setup-env.sh, and then hand off to apply.sh for
# the actual Helm release. Safe to re-run any time; every step here is a
# no-op if its own precondition already holds.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NAMESPACE="gaming-platform"
SECRETS_DIR="${TMPDIR:-/tmp}/gbp-k8s-secrets"

if ! kind get clusters 2>/dev/null | grep -qx gbp; then
  echo "Creating kind cluster 'gbp'..."
  kind create cluster --config "$REPO_ROOT/scripts/k8s/kind-config.yaml"
else
  echo "kind cluster 'gbp' already exists, reusing it."
fi

echo "Installing/updating Traefik..."
bash "$REPO_ROOT/scripts/k8s/install-traefik.sh"

kubectl get namespace "$NAMESPACE" >/dev/null 2>&1 || kubectl create namespace "$NAMESPACE"

if kubectl -n "$NAMESPACE" get secret identity-secrets economy-secrets email-service-secrets rabbitmq-secrets >/dev/null 2>&1; then
  echo "Secrets already applied in '$NAMESPACE', leaving them as they are."
else
  echo "Generating local-only secrets (never committed, live under $SECRETS_DIR)..."
  mkdir -p "$SECRETS_DIR"

  # Matches the RSA-2048/PKCS8 shape ADR-0017 and setup-env.sh already use for
  # the docker-compose .env: same key format, different delivery mechanism
  # (a Secret's stringData, not an escaped single .env line), so it's
  # generated directly as the real multi-line PEM a Secret can hold as-is.
  JWT_PRIVATE_KEY_PEM=$(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -outform PEM)
  IDENTITY_DB_PASSWORD=$(openssl rand -hex 16)
  ECONOMY_DB_PASSWORD=$(openssl rand -hex 16)
  RABBITMQ_PASSWORD=$(openssl rand -hex 16)

  sed \
    -e "s/change-me/${IDENTITY_DB_PASSWORD}/g" \
    "$REPO_ROOT/infra/helm/gaming-backend-platform/secrets.example/identity.yaml" \
    > "$SECRETS_DIR/identity-secrets.yaml.tmp"
  # The private key is multi-line and can't go through the same sed
  # substitution as the single-line values above; replace its placeholder
  # *key line* (anchored to the start of the line, not a bare substring
  # match, since the file's own comment a few lines up also mentions
  # "Jwt__PrivateKeyPem:" in passing, which a substring match would catch
  # too) with a proper YAML block scalar instead. awk, not python3, since
  # the latter isn't reliably present on every machine this script runs on.
  awk -v pem="$JWT_PRIVATE_KEY_PEM" '
    /^  Jwt__PrivateKeyPem:/ {
      print "  Jwt__PrivateKeyPem: |"
      n = split(pem, lines, "\n")
      for (i = 1; i <= n; i++) {
        if (lines[i] != "") print "    " lines[i]
      }
      next
    }
    { print }
  ' "$SECRETS_DIR/identity-secrets.yaml.tmp" > "$SECRETS_DIR/identity-secrets.yaml"
  rm -f "$SECRETS_DIR/identity-secrets.yaml.tmp"

  sed \
    -e "s/change-me/${ECONOMY_DB_PASSWORD}/g" \
    "$REPO_ROOT/infra/helm/gaming-backend-platform/secrets.example/economy.yaml" \
    > "$SECRETS_DIR/economy-secrets.yaml"

  sed \
    -e "s/RABBITMQ_DEFAULT_USER: \"change-me\"/RABBITMQ_DEFAULT_USER: \"gbp\"/" \
    -e "s/RABBITMQ_DEFAULT_PASS: \"change-me\"/RABBITMQ_DEFAULT_PASS: \"${RABBITMQ_PASSWORD}\"/" \
    "$REPO_ROOT/infra/helm/gaming-backend-platform/secrets.example/rabbitmq.yaml" \
    > "$SECRETS_DIR/rabbitmq-secrets.yaml"

  # No generated password here: the local cluster's mailpit needs no SMTP auth at
  # all, so the template's placeholders are blanked out to the empty values
  # SmtpEmailSender already treats as "skip AuthenticateAsync", not a real credential.
  sed \
    -e 's/"replace-with-smtp-username"/""/' \
    -e 's/"replace-with-smtp-password"/""/' \
    "$REPO_ROOT/infra/helm/gaming-backend-platform/secrets.example/email-service.yaml" \
    > "$SECRETS_DIR/email-service-secrets.yaml"

  kubectl apply -n "$NAMESPACE" \
    -f "$SECRETS_DIR/identity-secrets.yaml" \
    -f "$SECRETS_DIR/economy-secrets.yaml" \
    -f "$SECRETS_DIR/email-service-secrets.yaml" \
    -f "$SECRETS_DIR/rabbitmq-secrets.yaml"
fi

# observability's own namespace, same treatment as gaming-platform above:
# created here rather than declared as a chart resource, since Helm's
# --create-namespace only covers the release namespace and a chart-declared
# Namespace object would otherwise fight with this script over ownership.
OBSERVABILITY_NAMESPACE="observability"
kubectl get namespace "$OBSERVABILITY_NAMESPACE" >/dev/null 2>&1 || kubectl create namespace "$OBSERVABILITY_NAMESPACE"

if kubectl -n "$OBSERVABILITY_NAMESPACE" get secret grafana-secrets >/dev/null 2>&1; then
  echo "grafana-secrets already applied in '$OBSERVABILITY_NAMESPACE', leaving it as it is."
else
  mkdir -p "$SECRETS_DIR"
  GRAFANA_ADMIN_PASSWORD=$(openssl rand -hex 16)
  sed \
    -e "s/change-me/${GRAFANA_ADMIN_PASSWORD}/g" \
    "$REPO_ROOT/infra/helm/gaming-backend-platform/secrets.example/grafana.yaml" \
    > "$SECRETS_DIR/grafana-secrets.yaml"
  kubectl apply -n "$OBSERVABILITY_NAMESPACE" -f "$SECRETS_DIR/grafana-secrets.yaml"
fi

echo "Deploying the Helm release..."
bash "$REPO_ROOT/scripts/k8s/apply.sh"

echo ""
echo "Stack up. See docs/operations/deployment.md for the *.localhost hosts and seeded demo accounts."
