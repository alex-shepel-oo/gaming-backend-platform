#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT/infra"

echo "Starting player-client (compose will bring up its dependencies too)..."
docker compose up -d --build player-client
echo "player-client up."
echo ""
echo "player-client: http://localhost:8080"
