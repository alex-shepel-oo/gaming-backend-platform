#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
bash "$REPO_ROOT/scripts/all/setup-env.sh"
cd "$REPO_ROOT/infra"

echo "Starting full stack..."
docker compose up -d --build
docker compose ps
echo "Full stack up."
echo ""
echo "player-client: http://localhost:8080"
echo "admin-client: http://localhost:8081"
echo "Mailpit UI:    http://localhost:8025"
echo "RabbitMQ UI:   http://localhost:15672"
echo "Gateway:       http://localhost:5100"
