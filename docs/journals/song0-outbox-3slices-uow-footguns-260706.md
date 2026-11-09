# Journal — Sóng-0 outbox: 3 slices shipped + 2 CrossModuleUnitOfWork footguns

**2026-07-06** · Session: `/ask` microservices report review → 8 gating decisions → `/ck-plan` Sóng-0 → `/ck-cook` Phase-1 slice-by-slice. 3 commits LOCAL (not pushed).

## Session goal

Rà soát report chuyển đổi microservices → chốt decisions → bắt đầu implement Phase-1 (outbox at-least-once foundation, monolith-side, no runtime split).

## What worked (verified)

- **Rà soát report → 3 deep-dive grounded PHÁ 3 khẳng định sai của brainstorm `1918`** (audit-chain loss-invisible; authz shared-PermCodes = distributed-monolith; "0 2PC" có điều kiện DB-topology). 4 report + master-synthesis @`plans/reports/`.
- **8 gating decision LOCKED** (greenfield chưa-prod → làm ngay, thiết kế đúng từ đầu): S3=DB-per-service · hybrid-topology · dataSnapshot-envelope-encrypt · chain-receive-order · JWKS-local · signing-async-saga.
- **3 slice SHIP+commit** (slice-by-slice, mỗi slice scout→impl→review độc lập→fix→tester→commit):
  - `e03d2a4e` 1A outbox version-token (FullName resolver + disk-scan union).
  - `fd2cb118` 1B-1 Forms→outbox (`IOutboxWriter` primitive) + UoW H1 + nesting.
  - `9eefc562` 1B-2 Audit→outbox + UoW H2 + AuthServer DI + migration.

## What didn't / lessons

- **Code-review độc lập bắt 4 real blocking/high issue mà tests (244+) BỎ SÓT** — reinforces "tests-pass ≠ correct" cho data-integrity blast-radius:
  1. **1A map-completeness:** resolver AQN-fallback không cứu bare-FullName miss nếu `.Domain` assembly chưa load lúc build-map → silent event-loss reintroduced. Fix: disk-scan `*.Domain.dll` union (precedent DataClassificationService).
  2. **1B-1 H1:** `CrossModuleUnitOfWork.CommitAsync` commit raw-tx nhưng không báo EF → enlisted-ctx post-commit SaveChanges trúng committed-tx → **silent approval-workflow-link regression** (path 0 test-coverage). Fix: `ResetEnlistedContextsAsync` (`UseTransactionAsync(null)`).
  3. **1B-1 nesting:** SubmitForm unconditional BeginAsync → nested double-Begin dưới StartBP. Fix: ownsUow guard.
  4. **1B-2 H2:** AuditBehavior chạy sau inner-handler → AuditService trigger BeginAsync cycle-2 → code cũ mở conn mới, leak conn cũ. Fix: reuse open-connection.
- **CrossModuleUnitOfWork là footgun-magnet:** routing outbox qua nó phơi ra 2 latent bug (H1 post-commit, H2 cycle-2-leak) + 1 residual (M1 broken-conn fall-through). Shared-primitive changes = independent review bắt buộc.
- **Fullstack-developer agent đốt 300-450k token + self-park chờ slow-integration-test** (SQL Server cold-start). Chỉ thị "KHÔNG chạy full slow-universe inline, tester verify sau" cần thiết. 1 agent self-park 2 lần chưa ra report → tiếp quản verify trực tiếp (build+tree+source) thay resume.
- **AuthServer transitive-DI gap lần-3** (IMessageBus 04-28, IGuidGenerator 05-08, giờ ICrossModuleUnitOfWork/IOutboxWriter 07-06) — AuthServer skip `AddInfrastructure()`, arch-guard `AuthServerStartupValidationTests` bắt được.

## Decisions made

- **Contracts-reversal (Option A):** `SharedKernel.Contracts.Events` nay dep SharedKernel-core+MediatR cho integration-events (`IExternalDomainEvent`); client-interfaces vẫn pure. Accepted reversal (extraction-time events=shared messaging-NuGet). Arch-isolation xanh.
- **`IOutboxWriter` primitive** thay vì retrofit FormSubmission/AuditLogEntry → IAggregateRoot: escape-hatch enqueue trong active UoW, reusable cho mọi non-aggregate publisher (1B-3 lockout kế thừa).
- **1B-2 full-path (không YAGNI-defer):** user chọn consistent-mechanism dù audit-consumer NOOP (value thuần S2-prep).

## Test status

Mỗi slice green trước commit: 1A 25/25 outbox · 1B-1 arch-150 + atomicity-3 + BP-tx-6 + Forms-55 + Notif-26 · 1B-2 audit-262 + audit-integ-68 + atomicity-5. Build 0W/0E warnaserror mọi commit. Integration = real SQL Server (không InMemory — `WebAppFixture:95` strip OutboxProcessor false-green).

## Next session

1. `FORMS-PUBLIC-SUBMIT-500-TENANT-AGNOSTIC` — quick fix (`SubmitPublicFormCommand : ITenantAgnosticCommand`).
2. **1B-3 Lockout** — quyết cách adopt (ASP.NET Identity `UserManager` không CrossModuleUnitOfWork-friendly): (a) wrap OnLockoutAsync trong UoW + enlist Identity-ctx, hay (b) khác. Reuse IOutboxWriter.
3. 1C idempotency (durable inbox / consumer dedup) · 1D tenant-from-payload+PII dispatch-scope · 1E test.
4. Review + push 3 commit local khi user sẵn sàng.

Plan: `plans/260706-1237-song0-foundation-outbox-atomicity-authz-decouple/`. Memory: `monday-260706-song0-outbox-3slices-shipped-uow-footguns`.
