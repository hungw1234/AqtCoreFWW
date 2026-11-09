# Journal: Phase-06/6C Apply Step Shipped (2026-05-05 11:00 ICT)

**Branch:** `feature/foundation-perf-surgery` (NOT merged main; +16 ahead of origin)
**Session length:** ~75 min
**Plan:** `plans/260503-0045-foundation-perf-surgery/phase-06-drop-and-regen-migrations.md` (PARTIAL SHIPPED)

## What shipped (5 commits)

| SHA | Scope |
|-----|-------|
| `a809addf` | feat(phase-06/6c): apply deliverables — `apply-page-compression.sql` + `run-migrations.sh` 10→19 + 3 design-time factories |
| `995b6b22` | fix(phase-04b): Workflow IRepository<T,Guid> → <T,long> on 3 T1 entities |
| `c5a0a5cf` | fix(phase-04d): 12 datetime2(3) misses on T3 plain-class configs + 4 migrations regen |
| `ef975c7b` | test(phase-06/6d): SchemaUniversalsTests — 4 gates GREEN |
| `6f043732` | docs(phase-06): roadmap v2.135 + changelog + dependency-map regen procedure |

## Decisions and what they cost

### 1. Out-of-scope expansion three times

The session started as "execute remaining Phase-06 apply steps." Expanded three times:

1. **Workflow DI Guid→long fix** — 10 files. Caught when `dotnet run --project Api` failed `ValidateOnBuild` for 3 T1 handler resolutions.
2. **12 datetime2(3) entity-config misses** — 12 file edits + 4 migration regens. Caught by `SchemaUniversalsTests` first run.
3. **2 test bugs** — wrong table name (`AuditLogs` vs `AuditLogEntries`) + wrong tenant-scoped sample (`NotificationLogs` not ITenantScoped). Caught on advisor review of 1st test failure list.

Total scope creep: ~25 files beyond the 4-file Phase-06 apply step. Justified each time per advisor's "≤ 5 hits = fix; ≥ 10 = file as new phase" rule. Fourth expansion (37 integration test failures) hit the limit and was filed as Phase-04e.

### 2. Dropped DI workaround

Considered `ValidateOnBuild=false` to unblock host startup. Rejected per memory `feedback-debug-root-cause-no-guessing` — would silently brick runtime when handlers resolve. Fixed root cause (3 module registrations + repository class signatures + handler/DTO/controller types).

### 3. `dotnet ef` → `dotnet-ef` switch

`dotnet tool restore` writes manifest entries + caches NuGet packages, but `dotnet ef` resolution falls through to "tool not available" when local manifest path is incomplete. Direct binary `dotnet-ef --version` works. Switched `run-migrations.sh` to invoke `dotnet-ef` directly. Local manifest gap not investigated; out of scope.

### 4. Skipped Step 6D (integration suite)

Plan §Success Criteria included "All existing integration tests pass against new schema." Suite ran 66/103 pass, 37 fail. Failure pattern: Dapper positional-record DTO materialization (`WorkflowDefinitionSummaryDto`) + likely more DI registration gaps. None of the failure stack traces reference files touched in this session — all pre-existing handler-layer drift from Phase-04b.

Per advisor's third recommendation, did NOT push through the 37-test fix (would be 4th out-of-scope expansion in one session — pattern broken). Filed as Phase-04e plan stub `plans/260505-1050-phase-04e-handler-layer-drift/plan.md`. Phase-06 marked PARTIAL SHIPPED in plan + roadmap.

## What worked

- **`SchemaUniversalsTests` as schema gate** — caught 3 real schema bugs (12 datetime2 misses + 2 test sampling errors) in first run. Plan §Implementation Step 10 deliverable paid for itself within minutes of authoring.
- **Advisor for scope decisions** — 4 advisor calls, each at a fork. Each one prevented a different rabbit hole.
- **Memory `aqtcorefw-template-dev-only`** — confirmed dev-DB drop authorized. No user prompt needed.
- **Memory `feedback-sqlcmd-quoted-identifier`** — pre-pended `SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;` to apply-page-compression.sql before discovering the requirement empirically.

## What didn't

- **Initial migrate-via-API-host attempt** — wasted ~3 min spawning `dotnet run` in background expecting MigrateAsync to apply migrations. Failed at DI ValidateOnBuild before MigrateAsync ran. Advisor's correction: `dotnet ef database update` uses `IDesignTimeDbContextFactory`, never builds full DI graph. Should have reached for `run-migrations.sh` first.
- **Test sample selections** — picked `audit.AuditLogs` (table is `AuditLogEntries`) and `notifications.NotificationLogs` (not ITenantScoped). Cost ~2 min to diagnose. Lesson: when authoring schema-introspection tests, query `sys.tables` first to verify name; query `ITenantScoped` implementers list to verify sample membership.

## Open follow-ups

- **Phase-04e**: 37 integration tests. Likely Dapper DTO drift + DI gaps. Estimated 6-12h based on Phase-04b/04d patterns.
- **Step 6D gate**: deferred until Phase-04e green.
- **`dotnet ef` local-tool resolution**: investigate why `dotnet tool restore` claims success but `dotnet ef` falls through. Likely `.config/dotnet-tools.json` + NuGet cache mismatch.
- **`run-migrations.sh` LF→CRLF warnings**: harmless on Windows; bash file should stay LF. `.gitattributes` rule could enforce.

## Plan status

- 6A ✓ (`9d165e00`) — pre-flight tag + datetime2(3) MFA timestamps
- 6B ✓ (`1f344588`) — drop dev DB + delete 217 migration files
- 6C ✓ (regen `9de1dd59`/`fb47eacb`/`d5c5e322` from prior session + `a809addf`/`995b6b22`/`c5a0a5cf`/`ef975c7b` this session) — 19/19 migrations applied, schema gate GREEN
- 6D ✗ DEFER to Phase-04e — full integration suite gate

Phase-06 marked PARTIAL SHIPPED. Branch ready for Phase-04e resumption. NOT mergeable to main until 6D green.
