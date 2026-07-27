#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
bash "$REPO_ROOT/scripts/all/setup-env.sh"
cd "$REPO_ROOT/infra"

SERVICES=(
  identity_db
  economy_db
  consul
  identity-migrator
  identity-service
  rabbitmq
  economy-migrator
  economy-service
  platform-worker
  api-gateway
  mailpit
)

echo "Starting backend services: ${SERVICES[*]}"
docker compose up -d --build "${SERVICES[@]}"
echo "Backend stack up."
echo ""
echo "Gateway:    http://localhost:5100"
echo "Mailpit UI: http://localhost:8025"
