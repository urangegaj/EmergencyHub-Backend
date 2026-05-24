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

## NotificationService: `EmergencyCreatedConsumer` bypasses shared idempotency helper

`EmergencyCreatedConsumer` uses an inline `AnyAsync` check instead of `NotificationIdempotency.ExistsAsync` like every other consumer. Functionally equivalent, but inconsistent. Also means the DB unique index on `(EmergencyId, Type, UserId, FromStatus, ToStatus)` provides no backstop here — PostgreSQL treats `NULL != NULL` in unique indexes, so only the app-level check prevents duplicates.

**Affects:** `NotificationService/Kafka/Consumers/EmergencyCreatedConsumer.cs`

---

## NotificationService: `FromStatus`/`ToStatus` columns repurposed as generic discriminators

`DepartmentCaseUpdatedConsumer` stores `departmentType` in `FromStatus` and `caseStatus` in `ToStatus`. `EmergencyAssignedConsumer` stores `departmentType` in `FromStatus` and `assignmentId` in `ToStatus`. Deduplication works correctly, but the column names are misleading — DB rows show values like `from_status = 'Fire', to_status = 'CLOSED'` with no relation to a status transition. Consider renaming to `Discriminator1`/`Discriminator2` or a single `EventKey` column.

**Affects:** `NotificationService/Models/Notification.cs`, `DepartmentCaseUpdatedConsumer.cs`, `EmergencyAssignedConsumer.cs`

---

## NotificationService: email templates are minimal inline strings

Email subjects are bare interpolated strings (e.g. `$"Emergency assigned to {departmentType}"`). The tasks called for user-facing prose templates ("Your emergency has been assigned", "New case assigned to you", etc.). No HTML body, no greeting, no actionable detail — just a terse subject line with an empty or generic body.

**Affects:** `NotificationService/Kafka/Consumers/EmergencyAssignedConsumer.cs`, `EmergencyStatusUpdatedConsumer.cs`, `DepartmentCaseUpdatedConsumer.cs`, `EmergencyCreatedConsumer.cs`

---

## Postgres port mismatch between appsettings and docker-compose

All three services connect to `Port=5433` in their `appsettings.json`, but `docker-compose.yml` exposes Postgres on `5432:5432`. Running services via Docker requires either updating the compose port mapping or the connection strings.

**Affects:** `PoliceService/appsettings.json`, `FireService/appsettings.json`, `MedicalService/appsettings.json`, `docker-compose.yml`.
