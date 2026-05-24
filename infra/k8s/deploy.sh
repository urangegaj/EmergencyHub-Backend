#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$SCRIPT_DIR/../.."
ENV_FILE="$ROOT/.env"

if [ ! -f "$ENV_FILE" ]; then
  echo "ERROR: $ENV_FILE not found."
  exit 1
fi

source "$ENV_FILE"

# ── Minikube ────────────────────────────────────────────────────────────────
if ! minikube status | grep -q "Running"; then
  echo "Starting minikube..."
  minikube start --cpus=4 --memory=3000 --disk-size=20g
fi

# ── Build images on host (uses cache), then load into minikube ───────────────
echo "Building images on host Docker..."
docker build -f "$ROOT/src/AuthService/Dockerfile"          -t emergency-hub/auth-service:latest         "$ROOT"
docker build -f "$ROOT/src/EmergencyService/Dockerfile"     -t emergency-hub/emergency-service:latest    "$ROOT"
docker build -f "$ROOT/src/PoliceService/Dockerfile"        -t emergency-hub/police-service:latest       "$ROOT"
docker build -f "$ROOT/src/FireService/Dockerfile"          -t emergency-hub/fire-service:latest         "$ROOT"
docker build -f "$ROOT/src/MedicalService/Dockerfile"       -t emergency-hub/medical-service:latest      "$ROOT"
docker build -f "$ROOT/src/AssessmentService/Dockerfile"    -t emergency-hub/assessment-service:latest   "$ROOT"
docker build -f "$ROOT/src/NotificationService/Dockerfile"  -t emergency-hub/notification-service:latest "$ROOT"
docker build -f "$ROOT/src/Gateway/Dockerfile"              -t emergency-hub/gateway:latest              "$ROOT"
docker build -f "$ROOT/infra/kafka-connect/Dockerfile"      -t emergency-hub/kafka-connect:latest        "$ROOT/infra/kafka-connect"

echo "Loading images into minikube..."
for img in \
  emergency-hub/auth-service:latest \
  emergency-hub/emergency-service:latest \
  emergency-hub/police-service:latest \
  emergency-hub/fire-service:latest \
  emergency-hub/medical-service:latest \
  emergency-hub/assessment-service:latest \
  emergency-hub/notification-service:latest \
  emergency-hub/gateway:latest \
  emergency-hub/kafka-connect:latest; do
  echo "  loading $img"
  docker save "$img" | (eval "$(minikube docker-env)" && docker load)
done

# ── Namespace + secrets + configmap ─────────────────────────────────────────
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

# ── Infra ────────────────────────────────────────────────────────────────────
echo "Applying infra..."
kubectl apply -f "$SCRIPT_DIR/infra/"

echo "Waiting for postgres..."
kubectl rollout status statefulset/postgres -n emergency-hub --timeout=180s

echo "Waiting for kafka..."
kubectl rollout status deployment/kafka -n emergency-hub --timeout=180s

# ── App services ─────────────────────────────────────────────────────────────
echo "Applying app services..."
kubectl apply -f "$SCRIPT_DIR/apps/"

echo "Waiting for auth-service..."
kubectl rollout status deployment/auth-service -n emergency-hub --timeout=180s

echo "Waiting for gateway..."
kubectl rollout status deployment/gateway -n emergency-hub --timeout=180s

# ── Debezium connector ───────────────────────────────────────────────────────
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
echo "  kubectl port-forward svc/gateway 8090:8080 -n emergency-hub"
echo "  http://localhost:8090"
