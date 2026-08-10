#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
bash "$REPO_ROOT/scripts/all/setup-env.sh"
cd "$REPO_ROOT/infra"

echo "Starting player-client and admin-client (compose will bring up their dependencies too)..."
docker compose up -d --build player-client admin-client
echo "player-client and admin-client up."
echo ""
echo "player-client: http://localhost:8080"
echo "admin-client:  http://localhost:8081"
