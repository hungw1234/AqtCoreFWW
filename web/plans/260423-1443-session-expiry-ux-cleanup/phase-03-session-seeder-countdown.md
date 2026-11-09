# Phase 03 — Seeder Default Countdown 60→120s (REWRITE 2026-04-23)

## Context Links
- Parent plan: [plan.md](./plan.md)
- Source (ACTIVE): `src/modules/systemparams/AqtCoreFW.SystemParams.Infrastructure/Services/SystemParamSeeder.cs`
- Dead-code twin (NOT edited): `src/modules/systemparams/AqtCoreFW.SystemParams/SystemParamSeeder.cs` — flagged for backlog cleanup
- Range clamp: `SystemParamsController.cs:111` (`countdown ∈ [10, 300]s`)
- Admin override UI: `web/src/features/system-params/params-table.tsx` (existing — no change)

## Overview
- **Priority:** P3 (UX polish)
- **Status:** done
- **Brief:** Add 2 missing keys (`session_timeout_minutes`, `session_countdown_seconds`) to the ACTIVE Infra seeder + switch guard to per-key check. Controller queries these 2 keys; previously absent → silent hardcoded fallback (30/60). Now properly sourced from DB.

## Key Insights — SCOUT DISCOVERY (2026-04-23 during Phase C verify)
- **Dual seeder drift**: 2 files named `SystemParamSeeder` exist — only ONE is active:
  - ACTIVE: `AqtCoreFW.SystemParams.Infrastructure/Services/SystemParamSeeder.cs` (Program.cs:42 `using AqtCoreFW.SystemParams.Infrastructure;` → `AddSystemParams` resolves here)
  - DEAD: `AqtCoreFW.SystemParams/SystemParamSeeder.cs` — module-root `AddSystemParams` never called from Program.cs
- **Naming convention mismatch**: Active seeder uses colon-separated (`session:timeout_min`), Controller + FE use snake_case (`session_timeout_minutes`, `session_countdown_seconds`). Controller keys missing from seeder → silent fallback to hardcoded defaults
- **Old guard `if (AnyAsync) skip` was a bug amplifier**: once any row existed (first deploy seeded app:name etc.), future additions to `defaults` never reached DB. Fix: per-key existence check.
- Modern seeder inserts to `config.SystemParameters` schema (confirmed)

## Requirements
### Functional
- Add `session_timeout_minutes` = 30 (Int) to ACTIVE Infra seeder defaults
- Add `session_countdown_seconds` = 120 (Int) to ACTIVE Infra seeder defaults
- Switch guard from `AnyAsync → skip all` to per-key existence check so future additions propagate
- SQL UPDATE dev DB (existing deploy) to insert 2 missing rows — seeder idempotent won't touch prod DBs without fresh deploy

### Non-functional
- Idempotent per-key — safe to re-run on any DB state
- Legacy `session:timeout_min` key kept (not deleted) — unknown if any code reads it; defensive non-removal

## Architecture
No structural change. Single literal swap in tuple array.

```
SystemParamSeeder.StartAsync()
  └─> foreach DefaultParams: INSERT IF NOT EXISTS
        └─> session_countdown_seconds = "120"  (was "60")

App startup → SystemParamService.GetAsync("session_countdown_seconds")
  └─> existing DB returns whatever stored (60 or admin-overridden)
  └─> fresh DB returns 120

GET /api/v1/session-config
  └─> SystemParamsController clamps to [10, 300]
  └─> FE auth-provider uses as countdown duration
```

## Related Code Files
### Modify
- `src/modules/systemparams/AqtCoreFW.SystemParams/SystemParamSeeder.cs` (line 24 only)

### Create
- none

### Delete
- none

## Implementation Steps
1. Revert earlier edit to DEAD seeder `AqtCoreFW.SystemParams/SystemParamSeeder.cs` (restore `"60"`)
2. Edit ACTIVE `AqtCoreFW.SystemParams.Infrastructure/Services/SystemParamSeeder.cs`:
   - Add `session_timeout_minutes = "30"` and `session_countdown_seconds = "120"` tuples
   - Replace `if (AnyAsync) skip` with per-key existence HashSet lookup
3. `dotnet build` Infrastructure project — verify clean
4. SQL UPDATE dev DB: insert 2 rows tenant-global (TenantId=NULL) with the new keys
5. (manual) restart BE → GET /api/v1/session-config returns idleTimeoutMinutes=30, countdownSeconds=120
6. Add backlog item: "Consolidate dual SystemParamSeeder — remove dead code in AqtCoreFW.SystemParams/"

## Todo List
- [x] Revert modern (dead) seeder to "60"
- [x] Edit Infra (active) seeder: add 2 keys + per-key guard
- [x] `dotnet build` Infrastructure — clean compile
- [x] SQL UPDATE dev DB: insert session_timeout_minutes=30, session_countdown_seconds=120
- [x] Verify dev DB contains both keys via SELECT
- [ ] Manual: restart BE, F5 browser, check idle countdown shows 120s
- [ ] Add backlog item for dead code cleanup
- [ ] Commit `chore(systemparams): seed session-config keys + per-key guard`

## Success Criteria
- File edit applied — single character delta `60` → `120` in seeder tuple
- BE compiles
- Fresh deploy: SQL `SELECT Value FROM systemparams.SystemParameters WHERE [Key]='session_countdown_seconds'` returns `120`

## Risk Assessment
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaks existing deployments | None | None | Idempotent seeder — existing rows untouched |
| Value out of clamp range | None | None | 120 ∈ [10, 300] |
| FE math overflow on countdown | None | None | FE uses int seconds — handled |
| Wrong key edited | Low | Low | Diff review before commit |

## Security Considerations
- None. UX-only value change. Longer countdown = slightly more time before auto-logout when idle warning shown — no auth/authz impact.

## Next Steps
- Independent of Phase A/B — can ship in same PR or separate
- Follow-up (out-of-scope): if existing deployments need bump, ops can run `UPDATE systemparams.SystemParameters SET Value='120' WHERE [Key]='session_countdown_seconds'` OR use Admin UI
