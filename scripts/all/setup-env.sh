#!/usr/bin/env bash
set -euo pipefail

# Idempotent: creates infra/.env from infra/.env.example if it doesn't exist yet,
# and fills in a real, local-only RSA-2048 signing key for Jwt__PrivateKeyPem if that
# line is still the placeholder (docs/adr/0017-rs256-and-jwks.md: only
# identity-service ever reads this value; Economy/Notification/the gateway
# validate against Jwt__JwksUri instead and need no secret at all). Never
# regenerates or overwrites an already-real key on a re-run, so running this
# again after a developer already has a working .env is a no-op for that line.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ENV_FILE="$REPO_ROOT/infra/.env"
ENV_EXAMPLE="$REPO_ROOT/infra/.env.example"
PLACEHOLDER="Jwt__PrivateKeyPem=replace-with-your-own-rsa-2048-pkcs8-pem-see-comment-above"

if [ ! -f "$ENV_FILE" ]; then
  echo "infra/.env not found, creating it from infra/.env.example"
  cp "$ENV_EXAMPLE" "$ENV_FILE"
fi

if grep -qxF "$PLACEHOLDER" "$ENV_FILE"; then
  echo "Generating a local-only RSA-2048 signing key for Jwt__PrivateKeyPem..."
  PEM_ONE_LINE="$(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -outform PEM 2>/dev/null | awk '{printf "%s\\\\n", $0}')"

  TMP_FILE="$(mktemp)"
  awk -v line="Jwt__PrivateKeyPem=${PEM_ONE_LINE}" '
    $0 == "'"$PLACEHOLDER"'" { print line; next }
    { print }
  ' "$ENV_FILE" > "$TMP_FILE"
  mv "$TMP_FILE" "$ENV_FILE"

  echo "Wrote a freshly generated key into infra/.env (gitignored, never committed)."
else
  echo "infra/.env already has a real Jwt__PrivateKeyPem, leaving it as is."
fi
