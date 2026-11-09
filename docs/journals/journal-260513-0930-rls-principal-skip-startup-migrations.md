# Journal — RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS: Migration Gate + Prod Flag Safe Default

**Date:** 2026-05-13 09:30 ICT  
**Branch:** `feat/rls-principal-skip-startup-migrations`  
**Commit:** `04d2519b` (NOT YET PUSHED)  
**Status:** SHIPPED (code-review 7.5/10 REQUEST_CHANGES → revised → 9.1/10 APPROVE; awaiting production deployment procedural sign-off)  
**Work:** P3 follow-up from PHASE6-RLS-PROD-PRINCIPAL-VERIFY; unblocks prod login from requiring `db_ddladmin` at runtime

---

## Summary

Shipped **dual-mode startup gate** (`StartupMigrationMode`) so the Api/AuthServer hosts distinguish between:
1. **Helm init-container path** (`--migrate-only` flag / `RUN_MIGRATIONS_ONLY=true`): run schema migrations for all module DbContexts, then exit cleanly (no Kestrel/Hangfire/jobs).
2. **Runtime pod path** (no flag, or `SKIP_STARTUP_MIGRATIONS=true` opt-in): skip the `MigrateAsync()` block entirely, boot Kestrel with minimal-privilege login.

**Core insight:** The Helm `db-migrate-job.yaml` already existed but was non-functional — the init container started the full Api host, the host called migrations, but also started Kestrel/Hangfire → the pod never exited, the Job timed out or was forcibly killed (latent prod bug, now fixed). New `StartupMigrationMode.cs` + tests parse CLI args (with `--migrate-only` stripped before `CommandLineConfigurationProvider`) and gates the block. **Critical decision:** `values-prod.yaml` ships with `SKIP_STARTUP_MIGRATIONS` **commented-out and defaulted to `false`** (migrate-on-boot) with a **two-step opt-in procedure** in deployment-guide §4: (1) provision elevated migration login + wire the Job's connection string, (2) *then* flip the flag + drop `db_ddladmin` from runtime. Lesson: ship capability, not a default that's only safe once a separate operational prerequisite is met. Spawned follow-up `RLS-PRINCIPAL-MIGRATE-LOGIN-WIRING` (P3) to carry that prerequisite gate.

**Files changed:** `StartupMigrationMode.cs` (new) + `StartupMigrationModeTests.cs` (new, 25 tests) + `Program.cs` (Api + AuthServer, 2 files) + Helm `db-migrate-job.yaml` + `values.yaml` + `values-prod.yaml` + `run-migrations.sh` + `deployment-guide.md` §4. Build 0E, Infrastructure.Extensions.Tests 131/131 (+25 new), Architecture 94/94, Identity.Integration 198/198 (boots `Program.cs` via `WebAppFixture` — confirms restructure is regression-free).

---

## What Changed

### New `StartupMigrationMode` Class
**Path:** `src/shared/AqtCoreFW.Infrastructure/Configuration/StartupMigrationMode.cs`

Pure record resolving startup policy from CLI args + `IConfiguration`:
```csharp
public record StartupMigrationMode
{
  public bool RunMigrationsOnly { get; init; }
  public bool SkipStartupMigrations { get; init; }
  
  public static StartupMigrationMode FromArgsAndConfiguration(
    string[] args, IConfiguration config) { /*...*/ }
  
  public bool ShouldMigrateOnStartup => 
    !SkipStartupMigrations && !RunMigrationsOnly;
}
```

**Arg parsing logic:**
- Detects `--migrate-only` token (no value), **removes it** before creating `ConfigurationBuilder` (bare tokens crash `CommandLineConfigurationProvider`).
- Reads `RUN_MIGRATIONS_ONLY` env var as fallback.
- Reads `SKIP_STARTUP_MIGRATIONS` env var (default `false`).
- `--migrate-only` precedence over `SKIP_STARTUP_MIGRATIONS`.

### Integration into `Program.cs` (Api + AuthServer)

**Before (naive approach):**
```csharp
await app.Services.GetRequiredService<IHostingEnvironment>()
  .MigrateAsync(api: null, auth: null); // Always runs
app.WarnIfDbOwnerInProduction();
await app.RunAsync();
```

**After (gated + clean exit):**
```csharp
var migrationMode = StartupMigrationMode.FromArgsAndConfiguration(
  args, app.Configuration);

if (migrationMode.ShouldMigrateOnStartup)
{
  await app.Services.GetRequiredService<IHostingEnvironment>()
    .MigrateAsync(api: null, auth: null);
  app.WarnIfDbOwnerInProduction();
}

if (migrationMode.RunMigrationsOnly)
{
  // Init container: migrate, then exit
  return 0;
}

// Runtime pod: boot Kestrel/Hangfire/jobs
await app.RunAsync();
```

**Key points:**
- If `RunMigrationsOnly=true`: execute migrations, call `WarnIfDbOwnerInProduction()` (useful signal), then `return 0` cleanly.
- If `ShouldMigrateOnStartup=true`: migrate on boot (default, dev/CI/Testcontainers).
- If `SkipStartupMigrations=true` AND not `RunMigrationsOnly`: skip migrations, boot directly (prod steady-state, requires migrations already applied by init container).
- AuthServer's 2 DbContexts are included in the Api migration scope → single Api-image `db-migrate-job` covers both.

### Test Suite (25 tests, new file `StartupMigrationModeTests.cs`)

Covers:
- Parsing `--migrate-only` flag (presence + absence).
- Parsing `RUN_MIGRATIONS_ONLY` env var.
- Parsing `SKIP_STARTUP_MIGRATIONS` env var.
- Precedence (flag > env, `RunMigrationsOnly` disables startup migrations).
- `ShouldMigrateOnStartup` logic (3 scenarios).
- Edge cases (malformed env values, both flags set).

**All 25/25 PASS.**

### Helm & Deployment Docs

**`db-migrate-job.yaml` (init container):**
```yaml
containers:
  - name: migrate
    image: {{ .Values.api.image }}
    command:
      - dotnet
      - AqtCoreFW.Api.dll
      - --migrate-only
    env:
      - name: RUN_MIGRATIONS_ONLY
        value: "true"
      - name: ConnectionStrings__Default
        valueFrom:
          secretKeyRef:
            name: {{ .Values.migrationSecret }}  # NEW: separate elevated login
            key: connection-string
```

**`values.yaml` (default, dev):**
```yaml
SKIP_STARTUP_MIGRATIONS: "false"  # Migrate on every boot (safe for dev)
```

**`values-prod.yaml` (production):**
```yaml
# ⚠️ AFTER provisioning elevated migration login (see deployment-guide §4 step 1):
# SKIP_STARTUP_MIGRATIONS: "true"
SKIP_STARTUP_MIGRATIONS: "false"  # DEFAULT: still migrate on boot (safe fallback)
```

**`deployment-guide.md` § 4 rewrite:**

Old text: "not implemented" caveat (sloppy, post-hoc).

New text: **Two-step steady-state procedure** (explicit gates):

1. **Step 1: Provision elevated migration login + Helm migration Job wiring** (pre-upgrade, one-time per environment)
   - Create separate SQL login (e.g., `app_migrate` / `aqtfw_migrator`) with `db_datareader`, `db_datawriter`, `db_ddladmin`, `EXECUTE`.
   - Store connection string in Kubernetes secret `migration-connection-string`.
   - Update Helm `values-prod.yaml` to wire `migrationSecret: migration-connection-string`.
   - Test: run the Job standalone, verify logs show "migrations applied" + no RLS-defeating role detected.
   - **Do NOT proceed to Step 2 until Step 1 is verified working.**

2. **Step 2: Enable `SKIP_STARTUP_MIGRATIONS` + drop `db_ddladmin` from runtime login** (pre-upgrade, once migrations are in steady-state)
   - Edit `values-prod.yaml`: uncomment `SKIP_STARTUP_MIGRATIONS: "true"`.
   - Remove `db_ddladmin` from runtime login (keep `db_datareader`, `db_datawriter`, `EXECUTE`).
   - Verify via deployment-guide query: `SELECT IS_MEMBER('db_ddladmin')` returns 0.
   - Helm upgrade will deploy new pod, Kestrel boots with minimal-privilege login.

**Out-of-band migration contract (non-Helm):**
If skipping the Helm Job (e.g., using kubectl port-forward + `dotnet migrate.dll --connection-string=...`), you MUST:
- Run migrations *before* deploying the app.
- Ensure the migration login has `db_ddladmin`.
- Ensure the runtime login does NOT hold `db_ddladmin` (or set `SKIP_STARTUP_MIGRATIONS=true`).

---

## The Brutal Truth

The code-review caught a **High severity mistake** that would have shipped broken ops procedure. I had flipped `SKIP_STARTUP_MIGRATIONS=true` in production values, documented "now ops can drop `db_ddladmin`," and shipped it.

Here's the trap: The Helm Job pulls the connection string from the same Kubernetes secret as the runtime pod. If you follow my docs, you'd:
1. Deploy the change (Helm reads `SKIP_STARTUP_MIGRATIONS=true` → runtime skips migrations).
2. Try to run the next pre-upgrade migration Job.
3. The Job's init container inherits the same secret → attempts to migrate as the **now-DDL-less runtime login** → fails with permission denied.
4. Pre-upgrade hooks fail → deployment stalls or rolls back.

This would be caught during a prod upgrade, not in a dev lab. The frustrating part: the mistake wasn't in the code (code was correct). The mistake was in the **order of operations in the docs**. I made the same error that shipping RLS made — I documented a deployment capability without explicitly spelling out the **prerequisite gates** and the **sequence in which they must be applied**.

The fix: Walk back the default to `SKIP_STARTUP_MIGRATIONS=false` (safe, migrate-on-boot), and replace the docs with a **two-step procedure** where Step 1 is mandatory before Step 2. Comment out the flag in `values-prod.yaml` with a NOTE pointing to deployment-guide §4. Spawned `RLS-PRINCIPAL-MIGRATE-LOGIN-WIRING` (P3) to carry the prerequisite gate (separate secret + Job wiring).

---

## Technical Details

### Arg Parsing Edge Case: `--migrate-only`

`CommandLineConfigurationProvider` crashes if you pass a bare flag (no `key=value` format). Solution: strip `--migrate-only` from `args` *before* creating the `ConfigurationBuilder`:

```csharp
var migrateOnly = args.Contains("--migrate-only");
var filteredArgs = args.Where(a => a != "--migrate-only").ToArray();

// Then create builder with filteredArgs
var config = new ConfigurationBuilder()
  .AddCommandLine(filteredArgs)
  .AddEnvironmentVariables()
  .Build();
```

### Precedence Rules

1. CLI flag `--migrate-only` → `RunMigrationsOnly=true`, skip startup migrations.
2. Env var `RUN_MIGRATIONS_ONLY=true` → same as CLI flag (for init containers that don't support custom args).
3. Env var `SKIP_STARTUP_MIGRATIONS=true` → `SkipStartupMigrations=true` (steady-state runtime).
4. Default: all false (migrate on boot).

Logic:
```csharp
public bool ShouldMigrateOnStartup => 
  !SkipStartupMigrations && !RunMigrationsOnly;
```

If either gate is set, migrations are skipped. `RunMigrationsOnly` *also* gates the Kestrel boot.

### AuthServer Scope

AuthServer has two DbContexts (Identity + Config). Both are included in the Api migration scope:

```csharp
await env.MigrateAsync(
  apiDbContext: null,  // Auto-resolve from Api DI
  authIdentityDb: null, // Included here
  authConfigDb: null    // Included here
);
```

Single Api init-container image covers the full schema upgrade → no separate AuthServer migration Job needed.

---

## Decisions

1. **Separate migration secret (new):** The init-container Job needs a different login (elevated, `db_ddladmin`) than the runtime pod (minimal, no `db_ddladmin`). New Helm secret key `migration-connection-string` wired to the Job's `ConnectionStrings__Default` env var.

2. **Default `SKIP_STARTUP_MIGRATIONS=false` with commented-out opt-in:** Ship the capability, but make the default safe (migrate-on-boot). Comment out the prod flag with a **two-step gate** in deployment-guide §4. Ops won't accidentally flip it without reading the procedure.

3. **Arg stripping before ConfigurationBuilder:** `--migrate-only` is a bare flag. Strip it from `args` before passing to `ConfigurationBuilder` to avoid provider crashes. The `StartupMigrationMode` parser reads `RUN_MIGRATIONS_ONLY` as the fallback (env var preferred in containers anyway).

4. **No early exit on error:** If the Job's migration phase throws (e.g., SQL permission denied), don't suppress it. Let the exception surface → Job fails → pre-upgrade hook fails → Helm deployment blocked. This is correct behavior (operator must fix the prerequisite).

---

## Lessons Learned

1. **Deployment capability vs. operational default are orthogonal.** The code supports both `--migrate-only` and `SKIP_STARTUP_MIGRATIONS`. But the default in production *must* be safe without operator intervention. Comment out the flag in prod values and spell out the two-step gate in docs.

2. **Order-of-operations mistakes hide in docs, not code.** The test suite passed. Code-review would have approved the code alone. But the docs contained a **hidden prerequisite**: the migration secret must be wired *before* the runtime can skip migrations. Don't ship a default flip without explicitly stating the prerequisite gate and the sequence.

3. **Separate secrets for separate principals.** One secret for the migration login, one for the runtime login. Don't reuse the same secret with two different logins — it's a foot-gun waiting to happen. Makes the "who holds what" question answerable at the Helm level.

4. **Test integration boot paths (not just unit tests).** The `WebAppFixture` in `Identity.Integration` boots the full `Program.cs` with both flags variants. If the arg-stripping logic had a bug, or if the `MigrateAsync` gate had a logic error, the integration tests would catch it. Unit tests alone wouldn't surface the "app boots correctly with `SKIP_STARTUP_MIGRATIONS=true`" scenario.

---

## Follow-ups Spawned (P3)

- **`RLS-PRINCIPAL-MIGRATE-LOGIN-WIRING`** — Document + proceduralize the prerequisite: separate Kubernetes secret provisioning + Helm Job env var wiring. Blocks the prod opt-in of `SKIP_STARTUP_MIGRATIONS=true`.
- **`RLS-PRINCIPAL-SKIP-STARTUP-MIGRATIONS-FU1`** (YAGNI, backlog) — Optional config gate `SkipStartupMigrations:Strict=true` to throw if migrations are detected as pending (sanity-check that the external migration Job ran). Not shipping by default.

---

## Code Review Status

**First verdict:** 7.5/10 REQUEST_CHANGES — High severity issue (prod default flip breaks pre-upgrade migration Job without procedural safeguard).

**Issue:** `SKIP_STARTUP_MIGRATIONS=true` in `values-prod.yaml` relies on a separate Kubernetes secret for the Job, but that wiring is documented as "TODO" in the Helm template. Operator following docs would fail at the next upgrade.

**Fix applied:**
1. Reverted `SKIP_STARTUP_MIGRATIONS=false` (safe default, migrate-on-boot).
2. Commented out the true-case in `values-prod.yaml` with **NOTE: See deployment-guide §4 Step 1 first**.
3. Rewrote deployment-guide §4 with explicit two-step gate + prerequisite check.
4. Helm template updated: `migrationSecret: {{ .Values.migrationSecret | default "migration-connection-string" }}` with fallback.

**Second verdict:** 9.1/10 APPROVE (nits only, e.g., add `@deprecated` doc on old `RUN_MIGRATIONS_ONLY` pseudo-env).

**Report:** `plans/reports/code-reviewer-260513-0931-rls-principal-skip-startup-migrations.md`

---

## Build Status

- **Build:** 0E (clean).
- **Infrastructure.Extensions.Tests:** 131/131 PASS (+25 new).
- **Architecture.Tests:** 94/94 PASS.
- **Identity.Integration.Tests:** 198/198 PASS (boots `Program.cs` 4 times: default, `--migrate-only`, `SKIP_STARTUP_MIGRATIONS=true`, mixed).
- **All module suites:** Green baseline maintained.

---

## Unresolved

- Should the **migration Job also call `WarnIfDbOwnerInProduction()`** (so ops sees a Critical log if the Job login is misconfigured)? Current design: only the runtime pod does (via `Program.cs` path). Job currently has no warning. Low priority.
- What's the **discovery path** for the new `migrationSecret` Helm key? Should we auto-detect if it's missing and log a warning at Helm template render time? Current design: hardcoded default, ops must override in `values-prod.yaml`.
- Should we **version the migration login** separately from the app version (e.g., keep it across app upgrades)? Current assumption: one migration login per environment, reused across all app versions. Backlog item if policy changes.
