#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "1/4 Backend build"
"$SCRIPT_DIR/../backend/build.sh"
echo "2/4 Backend test"
"$SCRIPT_DIR/../backend/test.sh"
echo "3/4 Frontend build"
"$SCRIPT_DIR/../frontend/build.sh"
echo "4/4 Frontend test"
"$SCRIPT_DIR/../frontend/test.sh"

echo "Verify done: backend and frontend build and test green, nothing deployed."
