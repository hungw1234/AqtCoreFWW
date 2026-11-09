# Journal — v2.133p4 ListTenants Fixup: Twin Bugs from Careless Refactor

**Date:** 2026-04-28 (17:00–17:40 ICT)
**Branch:** thiennd/relaxed-mendel-755a9b (worktree)
**Component:** Identity module, ListTenants query handler + FE AdminTenantSelector
**Severity:** High (API 500/400, breaks tenant-switch UX)
**Status:** Resolved (patch-level, pending commit)

## TL;DR

SystemAdmin hit "Chuyển tenant" button in FE header → toast "Không thể tải danh sách tenant" (catch block swallowed error). Two consecutive bugs in same handler, both from careless Phase 01 microservice-extraction refactor (2026-04-27):

1. **T-SQL reserved keyword `identity` not bracket-escaped** → 500 GOV_SYS_000 "Incorrect syntax near the keyword 'identity'"
2. **SQL UNION dedup semantic lost in LINQ merge** → 400 GOV_SYS_002 "An item with the same key has already been added"

Both in `ListTenantsQueryHandler.cs` (refactored to split OrgUnit + Identity queries). **Root cause:** post-split code didn't verify identifier escaping or preserve set-operation dedup. Fixed + regression test added. Build 0E/0W, all tests green.

## What Happened (Chronological)

### Phase 1: Root Cause Hunt
- FE endpoint: `GET /api/v1/admin/users/tenants` → `UsersAdminController.ListTenants` → `ListTenantsQueryHandler`
- Handler refactored 2026-04-27 (Phase 01 ModuleClient extraction prep): split original SQL UNION into 2 async tasks:
  - `IOrgUnitModuleClient.ListTenantRootsAsync()` → cross-tenant OrgUnits
  - Identity inline SQL on `identity.AspNetUsers` → tenant member count
  - Merged via LINQ join
- User reported: API returns 500, response JSON `"detail":"Incorrect syntax near the keyword 'identity'"`

### Bug #1: T-SQL Reserved Keyword Not Escaped
**File:** `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListTenants/ListTenantsQueryHandler.cs:35`

```csharp
// BROKEN:
FROM identity.AspNetUsers u

// FIX:
FROM [identity].[AspNetUsers] u
```

**Why:** `identity` is T-SQL reserved keyword (column constraint modifier). Parser choked. Codebase scan found 3 other Dapper files (`BtcAssignmentSeeder`, `BtcOrphanPurger`) already escaped correctly — refactor author missed this one.

**Emotional note:** Incredibly frustrating. We have escape-bracket patterns already in codebase, but whoever touched `ListTenantsQueryHandler` didn't grep for existing usage. YAGNI + muscle memory failure. This is the kind of defensive cross-file knowledge that separates careless refactors from surgical ones.

### Bug #2: UNION Dedup Semantic Lost
**File:** `ListTenantsQueryHandler.cs:37`

After fixing #1, API returned **400**: `"An item with the same key has already been added. Key: 00000000-0000-0000-0000-000000000001"`

SQL DB check revealed: tenant `00000000-...0001` (Bộ Nội Vụ) has **37 root OrgUnits** (ParentId IS NULL, Level=1, IsDeleted=0). Original SQL UNION implicitly deduped by (TenantId, ...). Refactored LINQ code:

```csharp
// BROKEN:
var roots = ...
roots.ToDictionary(r => r.TenantId)  // throws if duplicate TenantId

// FIX:
roots
  .GroupBy(r => r.TenantId)
  .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Name).First())
```

Selected 1 representative root per tenant (by Name, deterministic). Semantic match: report "Bộ Nội Vụ" once, not 37 times.

**Emotional note:** This one stung worse than #1. The refactor fundamentally changed query shape (set operation → join), and nobody validated that the dedup guarantees survive translation. Sets have invariants. LINQ doesn't know about "UNION dedup". This is a classic SQL→ORM footgun that code review should have flagged.

### Phase 2: Regression Test
Created `tests/regression/AqtCoreFW.Regression.Tests/Core/RawSqlSchemaEscapeRegressionTests.cs`:

**Purpose:** Scan all `src/**/*.cs` for raw SQL patterns `(FROM|JOIN|INTO|UPDATE) identity\.X` without bracket escape. Fail test if found.

**First run:** FAIL — `FindRepoRoot()` walked up to worktree root but `.slnx` lives in `src/AqtCoreFW.slnx`. Fixed: check parent + "/src" directory has `.slnx`. Test now **21/21 PASS** (new test + 20 existing).

**Why this matters:** T-SQL reserved keywords are a recurring footgun (identity, value, user, role, key, etc.). Next refactor, next dev won't remember this. Regression test = institutional memory.

### Phase 3: FE Error Visibility
**File:** `web/src/shared/components/admin-tenant-selector.tsx:56`

Replaced silent catch:
```typescript
// BEFORE:
catch {}

// AFTER:
catch (err) {
  console.error('[AdminTenantSelector] fetch tenants failed:', err);
  // ... show toast
}
```

Future debug: error pops into console. No more silent failures.

### Phase 4: Verification on Dev Stack
- Killed existing ports 5001/5002/5173 (Auth, API, FE Vite)
- Started fresh stack via `aqt-runapp` skill:
  - Auth (pid 21356, 5002)
  - API (pid 21912, 5001) — rebuild after fix
  - FE (pid 14288, 5173) — npm ci reinstall
- Curl test after fix #2: **200 OK** with response `[{Bộ Tài Chính, 10000 user}, {Bộ Nội Vụ, 51 user}]`
- Confirmed anh: "OK rồi"

## Technical Details

**Error messages captured:**

1. 500 GOV_SYS_000:
   ```json
   {
     "statusCode": 500,
     "detail": "Incorrect syntax near the keyword 'identity'. SQL Server parsing failed at Offset 24"
   }
   ```

2. 400 GOV_SYS_002 (before dedup fix):
   ```json
   {
     "statusCode": 400,
     "detail": "An item with the same key has already been added. Key: 00000000-0000-0000-0000-000000000001"
   }
   ```

**Files changed:**
1. `src/modules/identity/AqtCoreFW.Identity.Application/Queries/ListTenants/ListTenantsQueryHandler.cs` (2 fixes: bracket escape + dedup)
2. `web/src/shared/components/admin-tenant-selector.tsx` (error logging)
3. `tests/regression/AqtCoreFW.Regression.Tests/Core/RawSqlSchemaEscapeRegressionTests.cs` (NEW)
4. Memory: `~/.claude/projects/C--AqtCoreFW/memory/feedback-mssql-reserved-schema-escape.md` (NEW) + MEMORY.md index update

**Build results:**
- `dotnet build src/AqtCoreFW.slnx`: **0 errors, 0 warnings**
- `dotnet test --filter "Category!=Integration"`: all unit projects PASS
- Regression test suite: **21/21 PASS**

## Lessons Learned

### SQL UNION → LINQ Refactor Checklist
When splitting SQL set operations into LINQ:
1. **Verify identifier escaping** — reserved keywords must bracket. Grep existing patterns.
2. **Preserve dedup semantics** — UNION removes duplicates; naive LINQ join doesn't. Explicitly dedup.
3. **Validate cardinality assumptions** — if 1:1 assumed, test with dupe keys.
4. **Code review must validate translation** — not just syntax. Shape mattered.

### Defensive Patterns
- **Reserved keyword escape:** Always bracket `[identity]`, `[user]`, `[value]`, etc. in raw SQL. It's free insurance.
- **Regression tests for footguns:** This one merits permanent coverage. Other T-SQL reserved keywords in codebase are ticking time-bombs.
- **Error logging in catch blocks:** Silent failures = 10x debug time. Log stack traces to console + observability sink.

### Code Review Gaps This Round
Phase 01 refactor (2026-04-27) saw 80+ files touched but focused on assembly/namespace moves. Nobody validated that the semantic behavior of refactored queries survived. Next phase: code-reviewer should explicitly verify:
- SQL pattern translation (UNION → JOIN/LINQ shape matching)
- Test assertions for post-refactor queries (not just "tests pass", but "tests validate original semantics")

## Root Cause Analysis

**Why did this slip through Phase 01 review?**

1. **Scope creep blind spot:** Phase 01 was "ModuleClient extraction prep" — moving code, not modifying query logic. Refactor of ListTenantsQueryHandler felt mechanical (split into 2 queries, merge results).

2. **Insufficient translation validation:** Original SQL was single UNION statement (set operation). Refactored version is 2 async calls + LINQ merge. Nobody asked: "Does LINQ preserve UNION dedup?"

3. **No regression test for SQL patterns:** Bracket escape and reserved keywords are codebase hygiene, not covered by unit/integration tests. Pure luck that 3 other raw SQL methods escaped correctly.

4. **Catch block hid error:** FE component swallowed 500/400 response without logging. 40-minute debug session + user report could have been 5 minutes with proper error visibility.

## Next Steps

1. **Commit both fixes** → single commit (handler + FE + regression test)
   - Message: `fix(identity): ListTenants bracket escape + dedup on tenant roots (v2.133p4)`
   - Does NOT bump version; patch-level hotfix

2. **Optional: Version bump for roadmap** (v2.133p3 → v2.133p4)
   - If user wants to batch with other fixes, defer
   - If shipping standalone, update roadmap + changelog + memory

3. **Expand regression suite** (future)
   - Scan for other reserved keywords: `[user]`, `[role]`, `[key]`, `[value]`, `[table]`, `[column]`
   - Similar pattern match + automated flagging

4. **Code review enhancement** (process)
   - For SQL refactors: validate set-operation semantics survive translation
   - Checklist item: "Original query postconditions" (dedup, ordering, null-handling)

## Emotional Reality

This was frustrating because both bugs were preventable with minimal discipline:

- **Bug #1:** Already exists 3 times in the codebase (escaped correctly). Grep would have caught it in 30 seconds. No excuse.
- **Bug #2:** Classic SQL→ORM translation mistake. Not obvious to junior devs, but the pattern (UNION dedup) is SQL 101. Code reviewer should have asked.
- **FE error logging:** 40 minutes of guess-and-check could have been avoided. Silent catch blocks are technical debt waiting to explode.

The real kick in the teeth is that the refactor was supposed to be low-risk ("moving code, not changing behavior"). Turns out, moving SQL code without validating semantic translation IS behavior change. Lesson burned in.

## Verification Checklist

- [x] Root cause documented (reserved keyword, dedup loss)
- [x] Both fixes applied + tested on live stack
- [x] Regression test passes (21/21)
- [x] Build 0E/0W
- [x] FE error logging added
- [x] Memory notes created (feedback + MEMORY index)
- [ ] Git commit (pending — next step via git-manager)
- [ ] Roadmap/changelog bump (pending — user decision)
