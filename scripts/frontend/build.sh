#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT/frontend"

echo "Installing frontend dependencies..."
npm ci
echo "Building frontend (shared, then player-client)..."
npm run build
echo "Frontend build done."
