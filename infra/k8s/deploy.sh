#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$SCRIPT_DIR/../.."
ENV_FILE="$ROOT/.env"

MODE="${1:-}"
if [ "$MODE" != "local" ] && [ "$MODE" != "prod" ]; then
  echo "Usage: $0 <local|prod>"
  echo "  local — build images, load into minikube (no push to GHCR)"
  echo "  prod  — build images, push to GHCR, deploy with registry pull"
  exit 1
fi

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: $ENV_FILE not found."
  exit 1
fi

source "$ENV_FILE"

REGISTRY="ghcr.io/urangegaj/emergency-hub"

SERVICES=(
  "src/AuthService/Dockerfile:auth-service"
  "src/EmergencyService/Dockerfile:emergency-service"
  "src/PoliceService/Dockerfile:police-service"
  "src/FireService/Dockerfile:fire-service"
  "src/MedicalService/Dockerfile:medical-service"
  "src/AssessmentService/Dockerfile:assessment-service"
  "src/NotificationService/Dockerfile:notification-service"
  "src/Gateway/Dockerfile:gateway"
)

# ── Minikube ─────────────────────────────────────────────────────────────────
if ! minikube status | grep -q "Running"; then
  echo "Starting minikube..."
  minikube start --cpus=4 --memory=3000 --disk-size=20g
fi

# ── Build images ──────────────────────────────────────────────────────────────
echo "Building images..."
for entry in "${SERVICES[@]}"; do
  dockerfile="${entry%%:*}"
  name="${entry##*:}"
  docker build -f "$ROOT/$dockerfile" -t "$REGISTRY/$name:latest" "$ROOT"
done
docker build -f "$ROOT/infra/kafka-connect/Dockerfile" -t "$REGISTRY/kafka-connect:latest" "$ROOT/infra/kafka-connect"

if [ "$MODE" = "local" ]; then
  # ── Local: load images into minikube ────────────────────────────────────────
  echo "Loading images into minikube..."
  for entry in "${SERVICES[@]}"; do
    name="${entry##*:}"
    echo "  loading $REGISTRY/$name:latest"
    docker save "$REGISTRY/$name:latest" | (eval "$(minikube docker-env)" && docker load)
  done
  docker save "$REGISTRY/kafka-connect:latest" | (eval "$(minikube docker-env)" && docker load)
  PULL_POLICY="IfNotPresent"
else
  # ── Prod: push images to GHCR ───────────────────────────────────────────────
  echo "Pushing images to GHCR..."
  for entry in "${SERVICES[@]}"; do
    name="${entry##*:}"
    echo "  pushing $REGISTRY/$name:latest"
    docker push "$REGISTRY/$name:latest"
  done
  docker push "$REGISTRY/kafka-connect:latest"
  PULL_POLICY="Always"
fi

# ── Namespace + secrets + configmap ──────────────────────────────────────────
echo "Applying namespace..."
kubectl apply -f "$SCRIPT_DIR/namespace.yaml"

echo "Creating secrets..."
kubectl create secret generic emergency-hub-secrets \
  --namespace=emergency-hub \
  --from-literal=jwt-private-key="${JWT_PRIVATE_KEY}" \
  --from-literal=jwt-public-key="${JWT_PUBLIC_KEY}" \
  --from-literal=openai-api-key="${OPENAI_API_KEY:-}" \
  --from-literal=postgres-password="postgres" \
  --dry-run=client -o yaml | kubectl apply -f -

kubectl apply -f "$SCRIPT_DIR/configmap.yaml"

# ── Infra ─────────────────────────────────────────────────────────────────────
echo "Applying infra..."
kubectl apply -f "$SCRIPT_DIR/infra/"

echo "Waiting for postgres..."
kubectl rollout status statefulset/postgres -n emergency-hub --timeout=180s

echo "Waiting for kafka..."
kubectl rollout status deployment/kafka -n emergency-hub --timeout=180s

# ── App services (patch imagePullPolicy on the fly) ───────────────────────────
echo "Applying app services (imagePullPolicy=$PULL_POLICY)..."
for f in "$SCRIPT_DIR/apps/"*.yaml; do
  sed "s/imagePullPolicy: Always/imagePullPolicy: $PULL_POLICY/" "$f" | kubectl apply -f -
done

if [ "$MODE" = "local" ]; then
  echo "Restarting deployments to pick up freshly loaded images..."
  for entry in "${SERVICES[@]}"; do
    name="${entry##*:}"
    kubectl rollout restart deployment/"$name" -n emergency-hub 2>/dev/null || true
  done
fi

echo "Waiting for auth-service..."
kubectl rollout status deployment/auth-service -n emergency-hub --timeout=180s

echo "Waiting for gateway..."
kubectl rollout status deployment/gateway -n emergency-hub --timeout=180s

# ── Debezium connector ────────────────────────────────────────────────────────
echo "Waiting for kafka-connect..."
kubectl rollout status deployment/kafka-connect -n emergency-hub --timeout=300s

echo "Waiting for kafka-connect REST API to be ready..."
kubectl port-forward svc/kafka-connect 8083:8083 -n emergency-hub &
PF_PID=$!
trap "kill $PF_PID 2>/dev/null" EXIT

for i in $(seq 1 60); do
  if curl -sf http://localhost:8083/connectors > /dev/null 2>&1; then
    break
  fi
  echo "  waiting... ($i/60)"
  sleep 5
done

echo "Registering Debezium connector..."
curl -fsSL -X POST http://localhost:8083/connectors \
  -H "Content-Type: application/json" \
  -d '{
    "name": "debezium-emergencies",
    "config": {
      "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
      "database.hostname": "postgres",
      "database.port": "5432",
      "database.user": "postgres",
      "database.password": "postgres",
      "database.dbname": "emergency_db",
      "topic.prefix": "cdc",
      "table.include.list": "public.Emergencies",
      "plugin.name": "pgoutput",
      "slot.name": "debezium_emergencies",
      "publication.name": "debezium_emergencies_pub",
      "heartbeat.interval.ms": "5000",
      "transforms": "route",
      "transforms.route.type": "org.apache.kafka.connect.transforms.RegexRouter",
      "transforms.route.regex": ".*",
      "transforms.route.replacement": "cdc.emergencies"
    }
  }' || echo "Connector may already exist — skipping."

kill $PF_PID 2>/dev/null || true
trap - EXIT

echo ""
echo "All done."
echo "Access the gateway:"
echo "  Run: minikube tunnel (keep it running in a separate terminal)"
echo "  Then: http://localhost:8080"
