#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

echo "Running backend tests..."
dotnet test backend/GamingBackendPlatform.slnx
echo "Backend tests done."
