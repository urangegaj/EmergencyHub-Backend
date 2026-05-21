#!/usr/bin/env bash
set -euo pipefail

CONNECT_URL="${CONNECT_URL:-http://localhost:8083}"

curl -fsSL -X POST "$CONNECT_URL/connectors" \
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
  }'

echo ""
echo "Connector deployed."
