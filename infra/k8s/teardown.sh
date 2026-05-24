#!/usr/bin/env bash
set -euo pipefail

echo "Deleting namespace emergency-hub (all resources inside will be removed)..."
kubectl delete namespace emergency-hub --ignore-not-found

echo "Done."
