#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

echo "Building backend solution..."
dotnet build backend/GamingBackendPlatform.slnx
echo "Backend build done."
