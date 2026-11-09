# Organization Legacy Cleanup Session

**Date**: 2026-04-28 10:30  
**Severity**: Medium  
**Component**: Organization module (AqtCoreFW.Organization flat-layout → DDD 4-layer)  
**Status**: Resolved — v2.132p9 shipped  

## What Happened

Removed legacy `src/modules/organization/AqtCoreFW.Organization/` flat-layout monolith (48 classes) and migrated all test references to 4-layer DDD structure (Domain, Application, Infrastructure, Presentation layers). Plan was pre-red-team revised (R2, 7→3 phases). Executed via `/cook --auto` (auto-approve gates). Result: Build 0E/0W, 128/143 unit tests green (15 skip), 50/50 contract tests, 12/12 integration tests.

## The Brutal Truth

Trusted audit subagent's "verified clean" claims without sampling. That was a mistake. Three verification errors discovered mid-session proved subagent reports describe **intent, not outcome**. This cost 45 minutes of re-auditing and forced manual Grep/Read spot-checks on every major claim going forward. The plan itself had a subtle flaw about build atomicity that wasn't caught in red-team review — Phase 02 Steps 3-4 ordering assumed intermediate build-green states that don't exist in C# namespace resolution. Finally, docs-manager subagent marked dependency-map N/A when it actually needed a version bump (2.132p8→2.132p9). Same drift pattern from memory feedback file — claiming "no content change = no update needed" but missing frontmatter.

## Technical Details

### Subagent Verification Failures (3 cases)

1. **Legacy consumer audit claimed ZERO consumers** — actually found 7 test source files + 3 test csproj importing `AqtCoreFW.Organization`:
   - `AqtCoreFW.Organization.Tests` → `AqtCoreFW.Organization.UnitTests.csproj`
   - `AqtCoreFW.Organization.Integration` → `AqtCoreFW.Organization.IntegrationTests.csproj`
   - `AqtCoreFW.Organization.Contract` → `AqtCoreFW.Organization.ContractTests.csproj`
   - 7 test source files with explicit `using AqtCoreFW.Organization` statements
   - Subagent ran Grep with `--glob="src/**/*.cs"` — excludes test projects at root or test/* hierarchy

2. **Request DTO location claim** — audit reported "4 new DTOs in Application/DTOs". Grep verified: DTOs were **inline `record` types in Presentation.Controllers**, not separate Application files. Same functional outcome (tests passed), but location claim was factually wrong.

3. **Pattern 8 (Block C security) grep verification** — claimed "0 matches after cleanup". Did not actually run; claimed by inference. When manually executed, grep returned 16 CS1503 compile errors (namespace `AqtCoreFW.Organization` still resolvable from parent namespace walk despite usings update).

**Root cause:** Subagent reported "after completion" without running final verification. Used assumed state, not actual state.

### Namespace Resolution Gotcha — Phase 02 Atomicity

Plan Steps 3-4 order (RT #10 flagged atomic concern):
- **Plan claim:** Drop csproj ref → build green (namespace gone) → update usings (tidy) → delete files (clean)
- **Reality:** Dropping csproj ref FIRST still leaves namespace resolvable because C# namespace walk resolves parent `AqtCoreFW.Organization` via folder structure, even if project ref is gone. Result: **CS1503 "type not in scope" after usings update**
- **Tested fix:** Both orderings produce intermediate red. Final solution: atomic commit (both edits + visibility flip + file deletion) in single PR. "Build green between steps" guideline was infeasible.

**Lesson:** C# namespace resolution doesn't cleanly decouple from file layout. Future cross-module refactors must assume no intermediate green states when touching namespace hierarchies.

### Non-Issues (Verified Clean)

- **ApiFactory.cs:16** — Plan flagged as "missed in v1" (RT Finding #1). Direct Read showed already on NEW namespace `AqtCoreFW.Organization.Infrastructure.Persistence`. Plan claim was stale.
- **Block B (tenancy)** — Dapper: all queries parameterized `@TenantId`. EF: global filters apply to OrgUnit, UserOrgUnitAssignment, StaffPositionHistory. Cache key `org:tree:{tenantId}` scoped. Step 2 (security fix) did not fire (clean baseline).
- **Phase 01 audit:** All tenancy checks passed.

### Migration History Deferred

Phase 03 Step 2 (EF migration history reset) — Docker container named `docker-sqlserver-1` (compose-prefixed), not `sqlserver` as documented. SA password not in memory. Per plan Risk Assessment "if can't connect, defer + document" — accepted and documented. Integration tests still pass (use LOCAL_SQL=true fixture).

### Docs Drift: dependency-map version bump

Docs-manager subagent marked dependency-map as **N/A: "no entity/endpoint change this session"**. Content claim was correct (no structural changes). **BUT** version line `v2.132p8` required bump to `v2.132p9`. This mirrors `feedback-memory-update-frontmatter-and-body.md` pattern — subagent confused "content stable = no update" with "metadata (version) stable = no update". Detected via Grep, fixed in place.

## What We Tried

1. **Trusted subagent audit report at face value** → Failed; re-ran manual Grep/Read spot-checks on all 3 claim categories
2. **Followed plan's "build green between steps" guidance** → Infeasible due to C# namespace walk; switched to atomic commit strategy
3. **Skipped EF migration history reset** → Docker connection issues; documented deferral in risk log (acceptable per plan)

## Root Cause Analysis

1. **Subagent drift** — Audit agent reported final state without running post-cleanup verification. Grep filters may have excluded test projects. No confidence interval on claims.
2. **Plan's namespace ordering assumption** — Red-team review did not empirically test intermediate build states. C# namespace hierarchy is tightly coupled to file layout; file operations create long transitional periods of namespace ambiguity.
3. **Docs-manager version bump** — Subagent applied "no structural change = N/A" heuristic to metadata. Required explicit guidance: "frontmatter description AND version line MUST update on every version increment, even if content stable."

## Lessons Learned

1. **Trust but verify (mandate):** Subagent reports describe **intent**. After every mutating subagent call, run Grep/Read sample on at least one major claim per report category. Evidence must show matched line + line number (new version string or PR # required, not just file existence).

2. **C# namespace refactors are atomic:** No intermediate green states when touching namespace hierarchies + project refs. Plan future refactors as single-commit operations. Document expected intermediate red states in risk section.

3. **Docs-manager frontmatter updates:** Separate "file content" from "metadata (version, date, PR#)". N/A only valid if metadata genuinely unchanged (rare). Subagent prompts must explicitly list: "version line MUST bump, frontmatter description MUST sync, body sections MAY stay stable if no change." Mirror `feedback-memory-update-frontmatter-and-body.md` pattern.

4. **Plan staling in red-team windows:** ApiFactory.cs was already fixed in a prior commit but plan's scout pass was from earlier baseline. Red-team must verify baseline currency before approving plan. Recommend re-running critical scout checks immediately before executing, not hours before.

5. **JwtOrgContext visibility decision:** User chose public (per v1 convention). RT recommended internal (smaller API surface). Noted decision rationale (consistency over minimalism) for next security review cycle.

## Next Steps

1. **Memory update** — Add this session's subagent drift pattern to `feedback-verify-subagent-reports.md` with concrete examples (audit report failures, docs-manager version bump).
2. **Red-team guidance** — Update RT checklist to verify plan scout baselines immediately pre-execution, not accept stale baselines from hours prior.
3. **Docs-manager prompts** — Template all future docs-manager calls with explicit "version bump required on version increment, even if content stable."
4. **Follow-on:** EF migration history reset (Phase 03 Step 2) — revisit after Docker config documented; low priority since integration tests pass.

## Post-ship addendum (2026-04-28 ~10:40)

**Phase 03 Step 2 EF migration history check executed** (originally deferred due to wrong SA password guess). Ran:

```bash
MSYS_NO_PATHCONV=1 docker exec docker-sqlserver-1 \
  /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'YourStrong!Passw0rd' \
  -d AqtCoreFW -Q "SELECT MigrationId FROM organization.[__EFMigrationsHistory] ORDER BY MigrationId;"
```

Result — 3 rows in `organization.__EFMigrationsHistory`:
- `20260314051632_InitialCreate` — **LEGACY drift** (class file deleted in commit 4bad754b; record orphaned in local DB)
- `20260331030717_InitialCreate` — NEW (expected)
- `20260331030819_AddRlsPolicies` — NEW (expected)

**Decision:** leave LEGACY orphan as historical no-op record (per plan preference). Rationale:
- Local-dev DB only — no production impact.
- EF Core only applies migrations missing from `__EFMigrationsHistory`. The orphan record being present means EF will skip re-running the deleted migration even if class file existed (it doesn't — already deleted).
- Rewriting history (`DELETE FROM organization.__EFMigrationsHistory WHERE MigrationId='20260314051632_InitialCreate'`) is reversible but adds risk for zero benefit.

**Lesson reinforcement:** root cause of the original "defer" was guessing SA password instead of reading `memory/sqlserver-docker-dev.md` line 16. Memory had complete answer (container name, password, sqlcmd path, Git Bash escape pattern). Defer was avoidable. Frontmatter description tightened in same session to make runtime name + password location visible at quick-scan layer (not just body).

---

## Session Metadata

**Subagents used:** audit (1 pass + re-verify), tester (1 pass green), docs-manager (1 pass + drift fix)  
**Files changed:** 18 (7 test source + 3 test csproj + 8 doc/config)  
**Build outcome:** 0 errors, 0 warnings  
**Test outcome:** 128/143 unit (15 skip), 50/50 contract, 12/12 integration  
**Time to resolution:** ~3.5 hours (includes re-audit + atomicity fix)  
**Commits:** 1 (atomic Phase 02-03 combined)  
**Version deployed:** v2.132p9
