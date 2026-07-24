#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT/infra"

CLEAN=false
for arg in "$@"; do
  case "$arg" in
    --clean) CLEAN=true ;;
  esac
done

if [ "$CLEAN" = true ]; then
  echo "Stopping stack and removing volumes..."
  docker compose down -v
  echo "Pruning dangling images..."
  docker image prune -f
else
  echo "Stopping stack..."
  docker compose down
fi

echo "Stack stopped."
