# v2.133p4 — EF Migration for messaging.dead_letters Table

**Date**: 2026-04-28 09:15
**Severity**: High
**Component**: Infrastructure / Messaging / EF Migrations
**Status**: Resolved

## What Happened

v2.133p3 code-review (commit `53995fc7`) surfaced a critical gap: `MessagingDbContext` had **zero production EF migration**. The `messaging.dead_letters` table only existed via `EnsureCreated()` in the integration test fixture (`MessagingFixture.cs:104-107`). This meant `DeadLetterConsumer.cs:34` would throw `Invalid object name 'messaging.dead_letters'` the moment MassTransit's retry policy exhausted (5s/30s/5m sequence per `MessagingRegistration.cs:54-58`). Latent because retry exhaustion is rare in dev/test — a ticking time bomb in production.

## The Brutal Truth

We shipped a consumer that **cannot persist dead letters in production**. The discovery during code-review was luck — the integration tests don't catch this because they use `EnsureCreated`, which bypasses migration validation. This is exactly the kind of silent failure that hits in production at 2am with a cascade of lost message records and downstream chaos. Frustrating because the pattern is established in every other module (Audit, Cases, Forms, Alerting, ApiKeys) — we knew better.

## Technical Details

**Missing artifact:** `MessagingDbContext` had no migrations directory and no `DesignTimeDbContextFactory`.

**Error signature (production):**
```
System.Data.SqlClient.SqlException: Invalid object name 'messaging.dead_letters'
  at DeadLetterConsumer.Handle(DeadLetterMessage) db.SaveChangesAsync()
```

**Root data:** Dead letters table schema needed: Id (PK), MessageType (500), Payload (max), FailureReason (2000), OriginalQueue (200), ReceivedAt (datetimeoffset), Status (int), RetriedAt (nullable), QuarantineReason (1000 nullable) + 2 indexes (Status, ReceivedAt).

## What We Tried

Initial approach: Add EF migration manually via `dotnet ef migrations add InitialCreate`. Hit a tooling quirk on Windows: `dotnet tool restore` succeeded but `dotnet ef` still reported "Run dotnet tool restore". Workaround: invoke `$HOME/.dotnet/tools/dotnet-ef.exe` directly with full path. Lost ~5 minutes to PATH cache staleness.

## Root Cause Analysis

1. **Process failure:** Integration tests use `EnsureCreated`, which skips migration history validation. `EnsureCreated` never touches the migration pipeline, so a missing migration goes undetected in CI.
2. **Pattern deviation:** Every other module (Audit, Cases, Forms, etc.) has `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")` configured in `AddDbContext`. Messaging context had neither migration directory nor schema isolation — a red flag we should have caught during API+AuthServer wiring (v2.133p3).
3. **Latency masking:** Retry exhaustion is rare, so `DeadLetterConsumer.cs` ran successfully in dev/test without ever persisting a record. The bug was semantically invisible until failure.

## What We Fixed

**8 source/config files + 6 docs:**
- NEW `src/shared/AqtCoreFW.Infrastructure/Messaging/DesignTimeDbContextFactory.cs` — design-time factory (mirrors Audit module pattern)
- NEW `src/shared/AqtCoreFW.Infrastructure/Messaging/Migrations/20260428090407_InitialCreate.cs` + Designer + ModelSnapshot (generated via `dotnet ef migrations add`)
- MOD `src/shared/AqtCoreFW.Infrastructure/Messaging/MessagingRegistration.cs` — added `MigrationsHistoryTable("__EFMigrationsHistory", "messaging")` for schema isolation
- MOD `src/host/AqtCoreFW.Api/Program.cs:354` — `MigrateAsync` wire after IntegrationDbContext in startup block
- MOD `src/host/AqtCoreFW.AuthServer/Program.cs:249-256` — split single-context migration block; AuthServer also needs the table (v2.133p3 wired `AddMessageBus`)
- MOD `tests/integration/AqtCoreFW.Messaging.Integration.Tests/Fixtures/MessagingFixture.cs:102-110` — switched `EnsureCreated` → `MigrateAsync` (proves migration applies cleanly on fresh Testcontainers DB)

## Code-Review Findings

**P1 — Local test fixture persistence gotcha (documented, not coded):** Devs running messaging integration tests pre-v2.133p4 with `TEST_USE_LOCAL_SQL=true` will have `AqtCoreFW_Test_Messaging` DB with `messaging.dead_letters` created by `EnsureCreated` but no `messaging.__EFMigrationsHistory` row. New `MigrateAsync` throws "object already exists" on first run. **Workaround:** `DROP DATABASE AqtCoreFW_Test_Messaging` once. Documented in v2.133p4 changelog Upgrade Notes.

**P2 — Concurrent host startup:** EF acquires SQL Server app-lock per `MigrateAsync` — safe, k8s deploy sees second host block briefly during migration.

## Lessons Learned

1. **`dotnet-ef` PATH cache on Windows:** `dotnet tool restore` succeeds but `dotnet ef` remains stale in the same shell session. Invoke `$HOME/.dotnet/tools/dotnet-ef.exe` directly instead. Wasted ~5 minutes.

2. **`MigrationsHistoryTable` is a mandatory pattern:** Every module (Audit, Cases, Forms, Alerting, ApiKeys, Backup, Webhooks, Identity) uses schema-isolated history tables. Missing this pollutes `dbo` with cross-context history rows. **New contexts MUST set this on day 1.**

3. **`EnsureCreated` masks migration bugs:** Test fixtures using `EnsureCreated` never validate the migration script. Switching to `MigrateAsync` on fresh-container test runs is cheap coverage proving the migration applies cleanly. Worth doing for every context.

4. **Repeat mistake — journal must come before git-manager:** This was already documented in `feedback-journal-before-git-commit.md`. Today the v2.133p3 journal is **UNTRACKED on disk**, not in commit `53995fc7`. The cook finalize-gate ordering was violated: git-manager ran before journal-writer. Will be corrected by a follow-up commit including both v2.133p3 + v2.133p4 journals.

## Verification

- `dotnet build src/AqtCoreFW.slnx`: 0E/0W ✓
- `dotnet test tests/integration/AqtCoreFW.Messaging.Integration.Tests` (Testcontainers): 4/4 PASS in 22s ✓
- **TC-MSG-INT-002** (`PublishAsync_AlwaysFailCommand_GeneratesFaultAfterRetries`): green — proves retry exhaustion path
- **TC-MSG-INT-003** (`PublishAsync_AlwaysFailCommand_DeadLetterConsumerPersistsRecord`): green — proves dead-letter persist on freshly-migrated DB ✓

## Next Steps

1. **Immediate:** Follow-up commit including v2.133p3 + v2.133p4 journals (fix the journal-before-git ordering violation)
2. **Docs:** Update `docs/project-changelog.md` with v2.133p4 entry + Upgrade Notes (DROP DATABASE workaround)
3. **Memory:** Record this lesson in `memory/feedback-journal-before-git-commit.md` as enforcement reminder (repeat mistake = pattern not internalized)
4. **Future:** When onboarding new DbContext, add migration factory + `MigrationsHistoryTable` + integration test fixture switch to `MigrateAsync` as a checklist item

---

## Files Changed Summary

```
NEW:
- src/shared/AqtCoreFW.Infrastructure/Messaging/DesignTimeDbContextFactory.cs
- src/shared/AqtCoreFW.Infrastructure/Messaging/Migrations/20260428090407_InitialCreate.cs
- src/shared/AqtCoreFW.Infrastructure/Messaging/Migrations/20260428090407_InitialCreate.Designer.cs
- src/shared/AqtCoreFW.Infrastructure/Messaging/Migrations/MessagingDbContextModelSnapshot.cs

MOD:
- src/shared/AqtCoreFW.Infrastructure/Messaging/MessagingRegistration.cs
- src/host/AqtCoreFW.Api/Program.cs
- src/host/AqtCoreFW.AuthServer/Program.cs
- tests/integration/AqtCoreFW.Messaging.Integration.Tests/Fixtures/MessagingFixture.cs
```
