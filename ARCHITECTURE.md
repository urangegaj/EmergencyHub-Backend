# Emergency Hub — Architecture & Business Logic

## Overview

Emergency Hub is a distributed, multi-tenant emergency dispatch and monitoring platform. Citizens report emergencies, a city dispatcher assigns them to the appropriate department (police, medical, fire), department responders manage the response, and once resolved an AI-generated assessment report is automatically produced and made available on the frontend.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Language | .NET (C#) |
| API style | REST (gateway-facing) + gRPC (inter-service) |
| Message broker | Apache Kafka |
| CDC | Debezium (Postgres connector) |
| Database | PostgreSQL (one schema per service) |
| Cache / locks | Redis |
| AI | OpenAI API (assessment reports only) |
| Frontend | React + Context API |
| Auth | JWT (RS256), RBAC |
| Background jobs | .NET hosted services / Kafka consumers |
| Docs | Swagger / OpenAPI |

---

## System Architecture

```
                                    ┌─────────────────────────────┐
                                    │         API Gateway          │
                                    │  - JWT validation            │
                                    │  - Tenant context injection  │
                                    │  - REST → gRPC routing       │
                                    │  - Long polling endpoint     │
                                    │  - CancellationToken passthru│
                                    └──────────────┬──────────────┘
                                                   │ gRPC (all services)
        ┌────────────────┬────────────────┬────────┴────────┬────────────────┬────────────────┐
        ▼                ▼                ▼                 ▼                ▼                ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ Auth Service │ │  Emergency   │ │   Police     │ │   Medical    │ │     Fire     │ │  Assessment  │
│              │ │   Service    │ │   Service    │ │   Service    │ │   Service    │ │   Service    │
│ - register   │ │              │ │              │ │              │ │              │ │ (read-only   │
│ - login      │ │ - lifecycle  │ │ - police     │ │ - medical    │ │ - fire cases │ │  gRPC for    │
│ - RBAC       │ │ - assignment │ │   cases      │ │   cases      │ │ - units      │ │  reports)    │
│ - JWT issue  │ │ - auto-res.  │ │ - units      │ │ - units      │ │ - responders │ │              │
└──────┬───────┘ └──────┬───────┘ └──────┬───────┘ └──────┬───────┘ └──────┬───────┘ └──────┬───────┘
       │own DB          │own DB          │own DB          │own DB          │own DB          │own DB

─────────────────────────────────── Kafka Bus ──────────────────────────────────────────────
   Topics: emergency.created | emergency.assigned | department.case.updated
           emergency.status.updated | notification.send | cdc.emergencies (Debezium)
────────────────────────────────────────────────────────────────────────────────────────────

                                                              ┌──────────────────────┐
                                                              │ Notification Service │
                                                              │ - consumes notif.send│
                                                              │ - emails             │
                                                              │ - background jobs    │
                                                              └──────────────────────┘
                                                                       │ own DB

Assessment Service additionally consumes cdc.emergencies (Debezium) and calls OpenAI.
Debezium runs as a separate Kafka Connect process watching the Emergency Service DB.
```

---

## Services

### API Gateway
- Single entrypoint for all client traffic
- Validates JWT on every request, rejects invalid/expired tokens
- Extracts `city_id` (and `department` for Responder role) from JWT claims and injects as ambient tenant context for all downstream gRPC calls
- Routes REST calls to the appropriate microservice via gRPC
- Hosts the long polling endpoint for emergency status monitoring — passes the `since` version and timeout to Emergency Service via gRPC and streams the response back when a change occurs or the timeout elapses
- **Propagates the incoming HTTP request's `CancellationToken` to all outbound gRPC calls** so that client disconnects (especially during long polling) immediately abort the downstream operation and free resources
- Logging middleware records all requests with tenant, user, and timing info

### Auth Service
- Registration with role assignment (Citizen, Dispatcher, Responder, Admin); Responder registration additionally requires a `department` (POLICE, MEDICAL, FIRE)
- Multiple Dispatchers are allowed per city
- Login issues a signed JWT containing `user_id`, `city_id`, `role`, and — for Responders — `department`
- Token policy: access token expiry **15 minutes**, refresh token expiry **7 days**, refresh tokens stored in Redis and rotated single-use on each refresh
- RBAC enforced per endpoint via role claims; department-specific endpoints (e.g. `/api/police/cases`) check both role and department claim
- Stores users, roles, permissions, and city (tenant) records

### Emergency Service
- Core domain service — owns the emergency lifecycle
- When a citizen reports an incident, the Gateway calls Emergency Service via gRPC synchronously; Emergency Service creates the record, returns the emergency ID to the Gateway (and therefore to the citizen), then emits `emergency.created` to Kafka for downstream consumers
- Dispatcher uses this service to assign emergencies to departments
- Tracks every status transition and maintains a full audit trail via `EmergencyStatusHistory`
- Maintains an integer `version` on each `Emergency` that increments on every status change — used as the cursor for long polling (`?since={lastVersion}`)
- Emits `emergency.status.updated` to Kafka whenever the **emergency lifecycle** status changes
- **Auto-resolution (Option A):** consumes `department.case.updated` events from department services. When a department case transitions to a closed state, Emergency Service updates the corresponding `EmergencyAssignment.closed_at` and counts open assignments. When the count reaches zero, the emergency is transitioned to `RESOLVED`. Emergency Service does **not** consume its own `emergency.status.updated` events, removing self-consumption entirely
- Supports long polling internally — holds gRPC connections open (async/await + CancellationToken) until a status change occurs for the requested emergency or the timeout elapses; client reconnects immediately after each response
- Debezium watches this service's Postgres table; the `RESOLVED` status transition produces a CDC event on `cdc.emergencies` consumed by the Assessment Service. `CANCELLED` transitions are filtered out by the Assessment Service's consumer (no report generated for cancelled emergencies)

### Police / Medical / Fire Services
- Each manages its own department-specific data: case details, unit roster, and responder assignments
- Consume `emergency.assigned` from Kafka, filter by their own department type, and create the corresponding case record
- When a responder updates a case (in-progress, closed, etc.), the service emits a `department.case.updated` event to Kafka. Emergency Service consumes this for lifecycle decisions (transitioning the emergency to IN_PROGRESS on first dept activity, or to RESOLVED when all dept cases close)
- Redis distributed lock is acquired when assigning a unit to prevent race conditions where two emergencies could grab the same unit simultaneously; lock released when the unit becomes available again
- Expose department-specific case data via gRPC, queried by the gateway for frontend display

### Assessment Service
- Kafka consumer and gRPC server — consumes `cdc.emergencies`, exposes a gRPC read interface for report retrieval and a manual-retry RPC
- Filters CDC events to only process `RESOLVED` transitions (CANCELLED emergencies do not produce reports)
- **Idempotency:** before generating, checks whether an `AssessmentReport` already exists for the `emergency_id`. If it exists and status is `COMPLETED`, the event is acknowledged and skipped (handles Kafka at-least-once delivery and Debezium connector restarts)
- Fetches the full emergency timeline, assignments, responders, and response times from Emergency Service via gRPC
- Builds a structured prompt and calls the OpenAI API
- **Retry policy:** on transient OpenAI failure (rate limit, timeout, 5xx) — exponential backoff with max 3 attempts. On exhaustion, the `AssessmentReport` is persisted with `status = FAILED` and `last_error` populated
- Report lifecycle reflected in `AssessmentReport.status`: `PENDING` (created, OpenAI call in progress) → `COMPLETED` (response stored) or `FAILED` (after retry exhaustion)
- Exposes a manual retry RPC (called by frontend through gateway) which re-attempts the OpenAI call for a `FAILED` report — same retry policy applies
- Report is available for retrieval via `GetReport(emergencyId)` gRPC call from the gateway; frontend polls for it after the emergency reaches `RESOLVED`

### Notification Service
- Consumes the `notification.send` Kafka topic
- Handles email delivery (assignment notifications, resolution confirmations)
- Tracks background job state with retry logic and exponential backoff

---

## Emergency Lifecycle

```
                          ┌──────────────────────────────────────────┐
                          │                                          │
REPORTED ──► DISPATCHED ──► IN_PROGRESS ──► RESOLVED                │
    │            │                      └──► CANCELLED ◄────────────┘
    │            │
    └────────────┴──► CANCELLED (at any pre-terminal state)

Only RESOLVED triggers Debezium → Assessment pipeline (CANCELLED does not produce a report)
```

| Status | Who triggers it |
|---|---|
| `REPORTED` | Citizen (reporting an incident) |
| `DISPATCHED` | Dispatcher (after assigning to a department) |
| `IN_PROGRESS` | Department responder (on scene) |
| `RESOLVED` | Auto-triggered by Emergency Service when all assigned departments close their cases |
| `CANCELLED` | Dispatcher or Admin — valid from any non-terminal state |

**Terminal states:** `RESOLVED` and `CANCELLED`.
**Assessment trigger:** Only `RESOLVED` triggers the Debezium → Assessment pipeline. `CANCELLED` emergencies do not generate an assessment report; the frontend simply displays the cancelled status with no report section.

---

## Business Logic & Use Cases

### Citizen
- Reports an emergency by submitting type, description, and location
- Monitors their reported emergency via long polling — client polls for status changes and reconnects immediately after each response
- Can view the AI-generated assessment report once the emergency reaches a terminal state; polls for the report after seeing a terminal status

### Dispatcher (per city, multiple allowed)
- Has a full view of all active emergencies within their city
- Assigns an emergency to one or more departments (police, medical, fire) based on type
- Can cancel an emergency from any non-terminal state
- Can monitor department unit availability before dispatching
- Receives notifications when a department updates the status of an assigned emergency

### Department Responder (Police / Medical / Fire)
- Sees only emergencies assigned to their department
- Updates the status of their assigned case as it progresses (e.g. marks as in-progress when on scene, marks case as closed when done)
- Closing a case contributes to the auto-resolution check in Emergency Service — when all assigned departments close their cases the emergency transitions to `RESOLVED` automatically
- Manages unit availability status (available, busy, offline)

### Admin
- Manages city (tenant) configuration
- Creates and manages user accounts and role assignments
- Has read access across all entities within their city

### Assessment Flow (automated, no user action required)
1. Emergency reaches `RESOLVED` (cancelled emergencies skip this flow entirely)
2. Debezium detects the row change via Postgres WAL and publishes to `cdc.emergencies`
3. Assessment Service consumes the event, filters out non-RESOLVED transitions, and checks idempotency (skip if a `COMPLETED` report already exists for that emergency)
4. Assessment Service requests the full emergency data from Emergency Service via gRPC (timeline, all status transitions, assigned departments, responders, timestamps) and persists an `AssessmentReport` row with `status = PENDING`
5. A structured prompt is built and the OpenAI API is called with exponential-backoff retries (max 3 attempts). On success the report is updated to `COMPLETED` with the OpenAI response and the 1.00–10.00 response quality score. On exhaustion it is updated to `FAILED` with `last_error`
6. Frontend is already polling the emergency status; once it receives `RESOLVED`, it begins polling `GET /assessments/{emergencyId}`. The frontend handles the report `status` field:
   - `PENDING` → keep polling
   - `COMPLETED` → render the report inline and stop polling
   - `FAILED` → show an error message with a "Retry" button that hits the manual retry RPC on Assessment Service via the gateway

### Search & Filtering
- Dispatcher and Admin can filter active/historical emergencies by status, type, date range, and free-text on description or address
- Free-text search is backed by a Postgres **`tsvector` GIN index** on `Emergency(description, address)` for efficient `to_tsquery` matching — `ILIKE '%...%'` is avoided
- All filters are tenant-scoped server-side — `city_id` is never accepted as a query parameter

---

## Multi-Tenancy

- **Model:** Row-level, tenant = city
- Every tenant-scoped table carries a `city_id` column
- EF Core global query filters on every `DbContext` enforce `WHERE city_id = @currentCityId` automatically
- `city_id` is embedded in the JWT; gateway middleware extracts it and passes it via gRPC metadata to all downstream services
- No cross-tenant data access is possible at the ORM layer without explicitly bypassing the global filter

---

## Redis Usage

### Caching Strategy
**Read-through:** The application always reads from the cache. On a cache miss, the cache layer fetches from the database, populates the cache, and returns the result. The application never reads the database directly for cached resources.

**Write-through:** On every write, the application updates the database and the cache simultaneously before acknowledging the operation. Ensures the cache is never stale after a write.

**Eviction policy:** LRU (Least Recently Used) — when memory pressure is reached, the least recently accessed keys are evicted first. This naturally retains hot data (frequently accessed emergencies, active unit statuses) and drops cold data.

> **Note:** Write-through guarantees the cache is consistent after every write, but LRU eviction can remove a key at any time under memory pressure. A subsequent read on an evicted key triggers a cache miss and read-through repopulation from the DB — consistency is maintained but there is a momentary DB hit. This is expected behavior and does not break the strategy.

### Cached Resources

| Use case | Strategy |
|---|---|
| Unit assignment lock | `SET lock:unit:{unitId} NX PX 30000` — prevents race condition when two emergencies are dispatched simultaneously |
| Active emergencies per city | Read/write-through; evicted via LRU when memory pressure occurs |
| Department unit availability | Read/write-through; updated synchronously on every unit status change |
| JWT blacklist (logout) | Revoked token JTIs stored with TTL matching token expiry |

---

## Kafka Topics

| Topic | Producer | Consumer(s) |
|---|---|---|
| `emergency.created` | Emergency Service | Notification Service |
| `emergency.assigned` | Emergency Service | Police / Medical / Fire Services (filter by own department), Notification Service |
| `department.case.updated` | Police / Medical / Fire Services | Emergency Service (auto-resolve check + IN_PROGRESS lifecycle transition) |
| `emergency.status.updated` | Emergency Service | Notification Service (filters on terminal statuses for emails) |
| `cdc.emergencies` | Debezium | Assessment Service (filters for RESOLVED only) |
| `notification.send` | Any service | Notification Service |

> **Self-consumption note:** Emergency Service does **not** consume `emergency.status.updated`. Lifecycle transitions driven by department activity flow exclusively through `department.case.updated`. This eliminates the self-consumption infinite-loop risk.

---

## Infrastructure Requirements

### Debezium
Debezium does not embed into the application — it runs as a **Kafka Connect** cluster (a separate service). The following is required for CDC to function:

- Postgres must be configured with `wal_level = logical` (default is `replica` — must be explicitly set)
- A Kafka Connect worker must be running with the Debezium Postgres connector deployed and configured to watch the Emergency Service database
- The connector config specifies which tables to watch (at minimum `emergencies`) and which Kafka topic to publish CDC events to (`cdc.emergencies`)
- This must be included in `docker-compose.yml` as a `kafka-connect` service alongside Kafka and Zookeeper

### Local Development Stack (docker-compose)
Minimum services to run the full system locally:
- Zookeeper
- Kafka
- Kafka Connect (with Debezium Postgres connector)
- PostgreSQL (one instance, separate databases per service, or separate containers)
- Redis
- Each microservice container

---

## Database Models

Models are distributed across service-owned databases. Each service runs its own EF Core migrations independently.

### Auth Service DB
| Model | Key fields |
|---|---|
| `City` | id, name, region, country, is_active |
| `User` | id, city_id, email, password_hash, role_id, created_at |
| `UserProfile` | id, user_id, first_name, last_name, phone |
| `Role` | id, name |
| `Permission` | id, name, resource, action |
| `RolePermission` | role_id, permission_id |

### Emergency Service DB
| Model | Key fields |
|---|---|
| `Emergency` | id, city_id, reporter_id, type_id, status, version (auto-increment on every status change, used as long-polling cursor), location_lat, location_lng, address, description, created_at, resolved_at |
| `EmergencyType` | id, name (FIRE, MEDICAL, CRIME, OTHER) |
| `EmergencyStatusHistory` | id, emergency_id, status, changed_by, changed_at, notes |
| `EmergencyAssignment` | id, emergency_id, department_type, dispatcher_id, instructions, assigned_at, closed_at (nullable; set when the corresponding department case is closed, used by auto-resolve check) |

### Police Service DB
| Model | Key fields |
|---|---|
| `PoliceCase` | id, emergency_id, city_id, status, incident_type, notes, opened_at, closed_at |
| `PoliceUnit` | id, city_id, name, vehicle_plate, status (AVAILABLE, BUSY, OFFLINE) |
| `PoliceResponder` | id, user_id, city_id, unit_id, badge_number |

### Medical Service DB
| Model | Key fields |
|---|---|
| `MedicalCase` | id, emergency_id, city_id, triage_level, patient_count, notes |
| `MedicalUnit` | id, city_id, name, vehicle_plate, status |
| `MedicalResponder` | id, user_id, city_id, unit_id, specialisation |

### Fire Service DB
| Model | Key fields |
|---|---|
| `FireCase` | id, emergency_id, city_id, fire_class, affected_area_m2, notes |
| `FireUnit` | id, city_id, name, vehicle_plate, status |
| `FireResponder` | id, user_id, city_id, unit_id, rank |

### Assessment Service DB
| Model | Key fields |
|---|---|
| `AssessmentReport` | id, emergency_id, city_id, status (PENDING / COMPLETED / FAILED), prompt_snapshot, openai_response (nullable), response_rating (decimal 1.00–10.00, OpenAI-generated, nullable), retry_count, last_error (nullable), generated_at (nullable), model_used, tokens_used (nullable) |

### Notification Service DB
| Model | Key fields |
|---|---|
| `Notification` | id, city_id, user_id, type, message, read, created_at |
| `BackgroundJob` | id, type, payload, status, attempts, last_error, created_at, completed_at |

**Total: 22 models across 6 service databases.**

---

## Frontend Architecture (React)

### Pages
| Page | Accessible by |
|---|---|
| Login / Register | Public |
| Dashboard | All authenticated users (role-filtered content) |
| Report Emergency | Citizen |
| Emergency Detail | Any (live status via long polling, assessment report polled once terminal state reached) |
| Dispatcher Board | Dispatcher (all active emergencies in city, assign actions) |
| Department Cases | Responders (filtered to their department) |
| Admin Panel | Admin |

### Context API
| Context | Responsibility |
|---|---|
| `AuthContext` | Holds JWT, user info, city_id; provides login/logout; attaches token to all requests |
| `EmergencyContext` | Manages active emergency list, long polling loop lifecycle, real-time status updates |
| `NotificationContext` | Polls for unread notifications, exposes mark-as-read |

### Long Polling Flow (frontend)
On opening Emergency Detail, `EmergencyContext` starts a long polling loop: `GET /emergencies/{id}/poll?since={lastVersion}&timeout=30`. The request is held open server-side until a status change occurs or 30 seconds elapse. On response, the context updates state and immediately fires the next request — creating a continuous near-real-time update loop.

When the emergency reaches a terminal state:
- `CANCELLED` → the emergency polling loop stops. The UI displays a "Cancelled" banner. No assessment report is fetched or shown.
- `RESOLVED` → the emergency polling loop stops and a second polling loop begins on `GET /assessments/{emergencyId}`. The frontend reacts to the `status` field on the response:
  - `PENDING` → keep polling
  - `COMPLETED` → render the report inline and stop polling
  - `FAILED` → stop polling, show an error message with a "Retry" button. Clicking Retry calls `POST /assessments/{emergencyId}/retry` and resumes polling

---

## CI/CD & Testing

- **Unit tests:** xUnit, per service, covering domain logic and ORM query filters
- **Integration/API tests:** Testcontainers (Postgres + Kafka in Docker) for end-to-end flow per service
- **CI pipeline:** GitHub Actions — build, test, lint, Docker image build on every PR
- **CD:** Docker Compose for local development; each service has its own `Dockerfile`
- Swagger auto-generated and served at `/swagger` on the gateway in non-production environments

---

## Repository Structure

```Test
emergency-hub/
├── EmergencyHub-Backend/
│   ├── src/
│   │   ├── Gateway/
│   │   ├── AuthService/
│   │   ├── EmergencyService/
│   │   ├── PoliceService/
│   │   ├── MedicalService/
│   │   ├── FireService/
│   │   ├── AssessmentService/
│   │   ├── NotificationService/
│   │   └── Shared/              # Proto definitions, Kafka contracts, common DTOs
│   ├── tests/
│   │   ├── AuthService.Tests/
│   │   ├── EmergencyService.Tests/
│   │   └── ...
│   └── docker-compose.yml
└── EmergencyHub-Frontend/
    ├── src/
    │   ├── contexts/
    │   ├── pages/
    │   ├── components/
    │   ├── services/            # API client wrappers
    │   └── hooks/
    └── ...
```
