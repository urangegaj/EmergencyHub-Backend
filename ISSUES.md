# Tech Debt

Issues shared across PoliceService, FireService, and MedicalService unless noted.

---

## Kafka publish is outside a transaction

`SaveChangesAsync` succeeds before `ProduceAsync` is called. If Kafka is unavailable at that point, the DB is updated but the event is silently dropped — no rollback, no retry. Consumers relying on `department.case.updated` can miss updates.

**Affects:** `PoliceGrpcService`, `FireGrpcService`, `MedicalGrpcService` — `UpdateCase` method.

---

## `UpdateUnitStatus` accepts any-to-any status transition

Calling `PUT /api/{service}/units/{id}/status` with `{ "status": "DEPLOYED" }` bypasses the case-assignment flow. A unit can be marked `DEPLOYED` with no case attached, breaking the invariant that `DEPLOYED` means the unit is working a case.

**Affects:** `PoliceGrpcService`, `FireGrpcService`, `MedicalGrpcService` — `UpdateUnitStatus` method.

---

## Postgres port mismatch between appsettings and docker-compose

All three services connect to `Port=5433` in their `appsettings.json`, but `docker-compose.yml` exposes Postgres on `5432:5432`. Running services via Docker requires either updating the compose port mapping or the connection strings.

**Affects:** `PoliceService/appsettings.json`, `FireService/appsettings.json`, `MedicalService/appsettings.json`, `docker-compose.yml`.
