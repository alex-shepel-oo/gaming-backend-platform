#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT/infra"

echo "Starting full stack..."
docker compose up -d --build
docker compose ps
echo "Full stack up."
echo ""
echo "Gateway:       http://localhost:5100"
echo "player-client: http://localhost:8080"
echo "Mailpit UI:    http://localhost:8025"
echo "RabbitMQ UI:   http://localhost:15672"
