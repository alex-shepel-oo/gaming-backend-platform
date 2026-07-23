#!/usr/bin/env bash
set -euo pipefail

echo "Deleting kind cluster 'gbp'..."
kind delete cluster --name gbp
echo "Cluster removed."
