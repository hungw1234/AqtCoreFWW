# v2.133p23: AuthServer Hangfire crash + 2 pre-existing arch fails — fold-in scope creep incident

**Date:** 2026-05-02 15:27–16:34 (Asia/Saigon)
**Severity:** P1 (initial) → P2 (post-fix scope creep)
**Component:** AuthServer bootstrap, Hangfire DI, Identity migrations, Audit cross-module refs
**Status:** RESOLVED — 24/24 arch tests PASS, smoke test verified

---

## What Happened

User reported every login on AuthServer crashing with `InvalidOperationException: Current JobStorage instance has not been initialized`. Root cause: `HangfireAuditJobEnqueuer.EnqueueHmacChain` uses static `BackgroundJob.Enqueue<T>()` which reads `JobStorage.Current`; AuthServer manually wired the audit pipeline but **forgot to call `AddHangfire()`**. 

The P1 fix took 5 minutes. The 1.5-hour tail was me + user folding in 2 pre-existing architecture failures that the repair process exposed, plus a Hangfire lazy-DI gotcha that the simple fix didn't fully address.

---

## The Brutal Truth

This was frustrating in three separate ways:

1. **The P1 was embarrassingly simple.** Manual audit wiring in AuthServer skipped one line (`AddHangfire`). Architecture test suite didn't catch it (static API gap). I felt dumb for missing the guard during finalize.

2. **Scope creep + decision cascade.** At 15:40, user offered to fold 2 pre-existing arch test failures into v2.133p23. I said yes. Should have pushed back: "Let's ship P1 first, then plan Phase-04 dependencies separately." Instead we did both. That added a migration remove that I executed **without reading the migration body** — a cardinal sin.

3. **The DI lazy-config trap.** Build + architecture tests both GREEN at 16:18. I was confident. User tried `/connect/authorize` — *same exception*. That moment checking logs and realizing `AddHangfire` doesn't eagerly resolve `JobStorage.Current` without a `BackgroundJobServer` or explicit `GetRequiredService` was humbling. Tests don't exercise the runtime path — they only check that the name exists.

The lesson: *green tests = safe to ship ONLY if tests exercise the actual runtime code path*. We had a test that `HangfireRegistrationConsistencyTests` parsed source files for string "AddHangfire" but never actually called `Enqueue()` at runtime.

---

## Technical Details

### Incident #1: P1 AuthServer Hangfire — 15:27

```
Login POST /connect/token
  → AuditService.LogAsync
  → IBackgroundAuditJobEnqueuer.EnqueueHmacChain
  → HangfireAuditJobEnqueuer.EnqueueHmacChain (using static BackgroundJob.Enqueue)
  → JobStorage.Current.get
  → throw: "not initialized"
```

**Why it slipped:** AuthServer `Program.cs:169-183` manually registers:
- `AddAudit()` (domain + app layer)
- `AddAuditInfrastructure()` — NO, it's not called! It's only `services.AddScoped<IBackgroundAuditJobEnqueuer, HangfireAuditJobEnqueuer>()`
- `services.AddSingleton<IBackgroundJobClient, NoOpBackgroundJobClient>()` — this only satisfies `IBackgroundJobClient` injection, it does NOT initialize `JobStorage.Current` for static API

**Fix:** Added full Hangfire client-only block (14 lines, no `AddHangfireServer`):

```csharp
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(Hangfire.CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new Hangfire.SqlServer.SqlServerStorageOptions
    {
        SchemaName = "hangfire",
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
        PrepareSchemaIfNecessary = false
    }));
// NO AddHangfireServer — enqueue-only host
```

**Guard:** New architecture test `HangfireRegistrationConsistencyTests.cs` (regex source scan): *Every host mentioning `IBackgroundAuditJobEnqueuer` or `HangfireAuditJobEnqueuer` MUST also have `AddHangfire(` or `AddHangfireJobs(` in the same `Program.cs` file.*

Reverting the block → test fails with AuthServer as offender. ✓

---

### Incident #2: Identity ModelSnapshot drift — 15:40

At 15:40, user offered: "While we're here, there are 2 architecture test failures — should we fold them in?"

I said yes (mistake #1).

Pre-existing failure: `MigrationConsistencyTests.AllModules_ModelSnapshot_Should_Match_LatestMigrationDesigner` — Identity. The `IdentityDbContextModelSnapshot.cs` had manual edits (extra `HasSentinel(true)`, mismatched `HasDefaultValueSql` vs `ValueGeneratedOnAdd` on bit columns) from EF version upgrades, drift from `20260501155717_AddMfaEmergencyDisableRequests.Designer`.

**What I did:**
1. `dotnet ef migrations add NoOpFixSnapshot` (regenerate snapshot from current model)
2. Saw Up/Down empty → assumed safe metadata-only → `dotnet ef migrations remove` (drop the no-op)

**The mistake:** I never opened the Up/Down body to READ it. It had real DDL.

**Result:** Migration 15:40 removed. Tests PASS. User starts API at 16:18. `Migrator.ValidateMigrations` throws `PendingModelChangesWarning` (EF Core detects model ≠ DB schema).

Root cause of THAT incident: Phase-02 (commit 35f33806) modified `ApplicationUser.MustChangePassword` entity config, **removed `HasDefaultValue("0")`** but didn't create a migration to DROP the DEFAULT constraint from the database. Drift accumulated silently for 1 day. My `migrations remove` deleted the ONLY place that drift was about to be fixed.

**The real DDL that was in that migration:**

```sql
ALTER TABLE [identity].[AspNetUsers]
DROP CONSTRAINT [DF_AspNetUsers_MustChangePassword];
```

And symmetric DOWN to re-add it.

**Fix:** Re-add migration (now named `20260502091913_SyncModelSnapshot.cs` per precedent) with actual DDL to drop default. Verify schema matches model. 

**Lesson file:** Created `feedback-no-op-migration-may-have-real-ddl.md` — *Always open and READ the Up/Down body before `migrations remove`. Empty Up/Down in visual diff viewer may hide real SQL if body is large. Naming precedent: `Sync*` for metadata-drift corrections that include DDL.*

---

### Incident #2b: Audit.Presentation cross-module refs — 15:50

Same arch-test batch. Secondary failure: `ModuleIsolationTests.NoModule_Should_DirectlyReference_AnotherModule` — Audit.Presentation is referencing `AqtCoreFW.Identity.Application.Authorization`. Test flagged Audit, but user pointed out: "9 modules reference this, why only Audit failed?"

Alphabetical scan + root cause hunt revealed the pattern: 7 authorization contracts lived in `Identity.Application.Authorization` namespace — `PermCodes` enum, policy types, `RequiresMfaAttribute`, etc. Nine module Presentation projects imported them directly.

**Systemic issue — not just Audit.** But cross-module contract sharing is legitimate (authorization policies are cross-cutting). Moving contracts to `SharedKernel.Authorization` is the right fix, but:

- Split `DepartmentAccessRequirement` marker interface to SharedKernel
- Split `DepartmentAccessHandler` + `IAttributeRuleReader` implementation to `Identity.Application/Authorization/DepartmentAccessHandler.cs` (handler needs `AttributeRule` Domain entity)
- Bulk update 78 `using` directives (9 modules × ~8 refs each)
- Delete 9 `<ProjectReference>` lines from module Presentation csproj files

**Result:** 24/24 arch tests PASS (was 22/24). ModuleIsolationTests now clean.

This was legit work, but folded into a P1 hotpatch. Not ideal for blame/audit trail but correct structurally.

---

### Incident #3: Hangfire lazy-config gotcha — 16:18

Build succeeds. Architecture tests PASS (24/24). AuthServer restarted.

User hits `/connect/authorize` → **same exception: "JobStorage instance has not been initialized"**.

**Root cause — DI lazy evaluation:** `services.AddHangfire(cfg => ...)` registers `IGlobalConfiguration` as a Singleton **factory**. The lambda that calls `UseSqlServerStorage` (which sets `JobStorage.Current`) only executes when the service is **resolved** from the container.

- **API host:** `AddHangfireServer` registers `BackgroundJobServer : IHostedService` → startup resolve → triggers config → `JobStorage.Current` set ✓
- **AuthServer (enqueue-only):** NO `BackgroundJobServer` → no resolve on startup → `JobStorage.Current` stays null → static `BackgroundJob.Enqueue` (in `HangfireAuditJobEnqueuer`) throws ✗

**The 1-line fix:**

```csharp
var app = builder.Build();
_ = app.Services.GetRequiredService<Hangfire.JobStorage>();  // Force eager resolve
```

Added post-Build, pre-routing. Smoke test `/connect/authorize` → HTTP 302 redirect (correct), zero `JobStorage instance has not` errors in logs.

**Why tests passed:** `HangfireRegistrationConsistencyTests` only checks that the `AddHangfire(` string exists in source. It never instantiates the app or calls `GetRequiredService`. 

**Lesson file:** Created `feedback-hangfire-addhangfire-lazy-config.md` — *`AddHangfire` DI registration is lazy. Enqueue-only hosts MUST explicitly `GetRequiredService<JobStorage>()` post-Build to initialize `JobStorage.Current` before any `BackgroundJob.Enqueue` static calls. `AddHangfireServer` automatically forces resolve; client-only hosts don't.*

Also reinforced existing lesson: `feedback-tests-pass-feature-fails.md` — *Build green + arch tests green ≠ runtime working. Smoke test with actual curl/browser is the only proof.*

---

## What We Tried

1. **AuthServer add `AddHangfire`** → Build green, arch test green (24/24) but runtime still throws
2. **Identity migration remove (mistake)** → Removed real DDL, surfaced hidden drift
3. **Manual auth contract move (legit)** → 78 file updates, systemic issue resolved
4. **Explicit JobStorage resolve** → Runtime finally works ✓

---

## Root Cause Analysis

Three separate root causes, compounded by one bad decision:

### RC1: AuthServer static Hangfire API without DI setup
- **Why:** Manual audit wiring incomplete. No code review catch on `AddHangfire` gap.
- **Why not caught earlier:** Architecture test suite only does string matching, doesn't exercise runtime paths.
- **Systemic:** Same class of bug as CSQ-6 (finalize gate missing). Could repeat with any static-API dependency.

### RC2: EF migration drift accumulation + wrong remove decision
- **Why drift accumulated:** Phase-02 removed `HasDefaultValue("0")` config but didn't create migration. Typical in rapid development without periodic schema reconciliation.
- **Why my remove was wrong:** I assumed empty Up/Down = metadata-only without READING the body. Cardinal sin in migration work.
- **Why not caught earlier:** No quarterly EF model snapshot regen routine. No lint rule "if you modify HasDefaultValue, add migration".

### RC3: DI lazy config in Hangfire + incomplete static-API initialization
- **Why:** Hangfire `AddHangfire` defers config callback until resolution. Enqueue-only hosts don't trigger resolution naturally.
- **Why not caught:** Test only checks string presence, not actual DI/runtime behavior.
- **Systemic:** Any static-config library without explicit initialization guard has this risk.

---

## Lessons Learned

### L1: Fold-in scope creep eats validation time
**Decision:** User offered "let's fold 2 arch failures too" at 15:40. I said yes thinking 10 min each = 20 min overhead. Reality: 90 minutes with 3 incidents. 

**Future:** When offered scope creep mid-hotpatch:
- "Separate PR recommended to keep audit trail clean." or 
- "Fold only if <= 10 min estimated and pre-tested."
Not both. Not blind.

**Reframe:** Hotpatch = singular issue + minimal guard test. Cleanup refactoring (cross-module moves) = separate PR. I violated this.

### L2: Read migration bodies before removing
**Decision:** `migrations remove` on what I thought was empty metadata. Didn't open the SQL.

**Future:** Non-negotiable:
```powershell
# Before remove:
dotnet ef migrations script --idempotent -o /tmp/preview.sql
# Read /tmp/preview.sql
# THEN decide
dotnet ef migrations remove
```

Also: naming convention `Sync*` for metadata-drift corrections that include DDL helps downstream developers (me, 6 months later) understand "this isn't safe to ignore".

### L3: Static API dependencies need explicit initialization guards in enqueue-only hosts
**Decision:** `AddHangfire` without `AddHangfireServer` seemed sufficient based on code review.

**Future:** 
- Architecture test: not just "string present", but verify it's called BEFORE first static API use
- Or: refactor `HangfireAuditJobEnqueuer` to inject `IBackgroundJobClient` instead of using static `BackgroundJob.Enqueue`. (Defer, but track.)
- Or: unit test that instantiates app + calls `GetRequiredService<JobStorage>()` explicitly

**Testing lesson:** Tests that pass don't prove the code works — they prove the code compiles. Green tests + green build can mask initialization order bugs. Smoke test with real HTTP request is the actual proof.

### L4: Periodic EF model snapshot regen recommended
**Finding:** Phase-02 (35f33806, 2026-05-01) modified entity config without migration. Takes 1 day to surface in development. Would compound in 3–6 month codebases.

**Future:** Quarterly `dotnet ef migrations add NoOp_QuarterlySnapshot --no-migrations` → regenerate snapshot, regen Designer, commit if any drift. Catches config drift early.

---

## Next Steps

### Immediate (blocking v2.133p23 ship)
1. ✅ AuthServer `AddHangfire` + JobStorage eager resolve
2. ✅ Identity migration re-add (`SyncModelSnapshot`)
3. ✅ Audit cross-module refs moved to SharedKernel.Authorization
4. ✅ All 24/24 arch tests PASS
5. ✅ Smoke test `/connect/authorize` + login flow works

### Follow-up (Phase-04 unblock)
1. Refactor `HangfireAuditJobEnqueuer` to inject `IBackgroundJobClient` (remove static API dependency)
2. Add `.AddHangfire(...)` health check to AuthServer HealthChecks chain
3. Update `docs/system-architecture.md`: ADR on "client-only Hangfire host pattern" (or defer to next major)
4. Update memory `feedback-architecture-tests-in-finalize-gate.md`: Add 2026-05-02 Hangfire static-API gap to guard examples

### Memory files created
- `feedback-no-op-migration-may-have-real-ddl.md` 
- `feedback-hangfire-addhangfire-lazy-config.md`
- This journal

---

## Final Scope

| Category | Files |
|----------|-------|
| Modified | 93 |
| Deleted | 2 (NoOpBackgroundJobClient.cs, Infrastructure RequiresMfaAttribute stub) |
| Renames (7 auth contracts) | ~12 locations |
| New | 3 (HangfireRegistrationConsistencyTests, DepartmentAccessHandler split, journal) |
| **Total touched** | ~110 |

| Test | Result |
|------|--------|
| `dotnet build AqtCoreFW.slnx` | ✅ 0 errors, 0 warnings |
| Architecture tests | ✅ 24/24 PASS (was 22/24) |
| HangfireRegistrationConsistencyTests guard | ✅ 1/1 PASS, counter-factual works |
| ModuleIsolationTests | ✅ PASS (was FAIL) |
| MigrationConsistencyTests | ✅ PASS (was FAIL) |
| Smoke test `/connect/authorize` | ✅ HTTP 302 redirect, no errors |

---

## Unresolved Questions

1. **PrepareSchemaIfNecessary** — currently `false` (API host owns schema). Acceptable for dev (user boots both), but AuthServer would crash on first solo run if `[hangfire]` table missing. Keep `false` or change `true` for dev ergonomics?

2. **ADR for client-only pattern** — Worth documenting "Hangfire client-only without server" as named pattern in `system-architecture.md`, or keep in code comments only?

3. **EF quarterly snapshot regen** — Schedule this as pre-commit hook, or manual reminder, or let future developer re-learn the hard way?
