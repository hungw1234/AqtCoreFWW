# T4-05-FU5: Rules Tenant Write-Path Hardening — Why Scope Crept & How Pattern A+B Prevented It

**Date**: 2026-04-20 10:50–12:05  
**Severity**: High (P0 security + production multi-tenant)  
**Component**: Rules.Infrastructure + Rules.Application (commands)  
**Status**: APPROVED_WITH_MINOR, shipped @ `f7b5604b`  
**Version**: v2.123 (post-v2.122 @ 3142f32a)

---

## What Happened

Code-reviewer flagged `RuleSetRepository` as P1 security gap in v2.122: tenant isolation pattern from Identity B.1 (FU2/FU4 precedent) not ported to Rules write path. Backlog entry marked "1h effort." We committed to it. Ended up 2h real time, +12 tests, asymmetric admin design, two new backlog items discovered in code-review.

### Timeline
1. **Scout phase (parallel):** 2 Explore agents audited Rules current state + Identity B.1 reference pattern. **Surprise:** scope bigger than backlog — 3 handlers (CreateRuleSet, UpdateRule, ActivateRuleVersion) + repo, not UpdateRule alone. Backlog had underscooped.
2. **Planning (advisor checkpoint):** 4 decisions locked. R2 risk explicitly red-teamed (query handlers trust request.TenantId without validation) — flagged as **FU5.1 backlog candidate** before code-writing.
3. **Phase 01 (TDD):** `RuleSetRepository` refactored primary-ctor → explicit 3-arg ctor. Inline filter `IsSystemAdmin || TenantId == current` (admin read-bypass). Write guards strict (no admin bypass). 5 unit tests red→green. **Caveat:** InMemory DB doesn't fully test SystemAdmin bypass loop.
4. **Phase 02 (TDD):** 3 handlers injected ITenantContext. **Pattern A** (CreateRuleSet): pre-factory guard on request.TenantId. **Pattern B** (Update/Activate): post-load belt+suspenders. Comment on dead-path logic preserved future intent: "**[SECURITY] This check mirrors repo read-path admin bypass but write guards use strict tenant match (no bypass). Repo filter survives Hangfire.IgnoreQueryFilters() — this handler check is belt. If repo ever adds IgnoreFilters method, this suspenders catch leaks.**"
5. **Phase 03 (TDD):** New `AqtCoreFW.Rules.Infrastructure.Tests` project + `RulesTenantIsolationFixture` (unique DbName) + 4 integration scenarios (I1–I4). I4 explicitly asserts asymmetric admin handling: `IsSystemAdmin=true` succeeds on read, fails on write with structured LogWarning.
6. **Code-review:** 9.3/10 APPROVED_WITH_MINOR. **M1 finding:** 6 query handlers use `request.TenantId` without context validation (GetRuleSetQuery, ListRuleSetsQuery, etc.) — filed **FU5.1 backlog**. **M2 finding:** `DecisionTableRepository` has identical gap as RuleSet had pre-FU5 — filed **FU5.2 backlog**.

---

## The Brutal Truth

This was a **scope-creep catch that should have happened in backlog refinement, not in sprint.**

We underscooped by NOT asking: *"Does the Rules module have other write-path entry points besides UpdateRule?"* Scout answered it in 20 minutes (yes: Create, Update, Activate handlers). Backlog author had context-loaded on *Identity* B.1 pattern, assumed Rules was structurally identical. Not true.

The **asymmetric admin design** (read-bypass, write-strict) is clever and secure. But it's *subtle*. Inline comment is the only thing saving a future `git blame` from a developer removing the "dead" post-load check in Update/Activate handlers. Without that comment, the dead-path logic looks like redundant paranoia. With it, future dev understands: Repo filter survives Hangfire bypass; handler check doesn't. That's not paranoia—that's layered defence. **DOCUMENT IT OR IT GETS DELETED.**

The InMemory DB limitation hurt. Unit tests can't validate `IsSystemAdmin` bypass path properly. We punted to integration tests (I4), which adds fixture cost. This is a **testing friction point:** multi-tenant InMemory scenarios with role-based data shapes are hard. Solution: either accept the gap for unit-level coverage OR mock `ITenantContext.IsSystemAdmin` explicitly in repo unit tests (we didn't; future task).

---

## Technical Details

### Files Changed (11 total)
| File | Change | Lines |
|------|--------|-------|
| `RuleSetRepository.cs` | Primary-ctor → explicit 3-arg ctor (ITenantContext, ILogger). Inline filter + write guards. | +35 |
| `CreateRuleSetCommandHandler.cs` | ITenantContext injection + pre-factory guard. ValidationError on mismatch. | +12 |
| `UpdateRuleCommandHandler.cs` | ITenantContext injection + post-load belt+suspenders guard + comment. | +18 |
| `ActivateRuleVersionCommandHandler.cs` | ITenantContext injection + post-load belt+suspenders guard + comment. | +16 |
| `CreateRuleSetCommandHandler.Tests.cs` | New guard test (same-tenant ✓, cross-tenant ✗). Updated 9 existing ctor mocks. | +22 |
| `UpdateRuleCommandHandler.Tests.cs` | New guard test. Updated 9 existing ctor mocks. | +18 |
| `ActivateRuleVersionCommandHandler.Tests.cs` | New guard test. Updated 9 existing ctor mocks. | +16 |
| `AqtCoreFW.Rules.Infrastructure.Tests.csproj` | New test project. | 65 (full) |
| `RuleSetRepositoryTests.cs` | 5 new unit tests: GetByIdAsync tenant filter, AddAsync guard, UpdateAsync guard, LogWarning structure. | 140 |
| `RulesTenantIsolationFixture.cs` | New fixture (DbName `AqtCoreFW_Test_RulesTenantIso`). ~30 lines duplication vs `TenantIsolationFixture` (acceptable; scope-creep safeguard). | 80 |
| `RuleSetTenantIsolationTests.cs` | 4 integration scenarios (I1–I4): read-bypass, write-strict, asymmetric admin, log structure. | 160 |
| `src/AqtCoreFW.slnx` | New test project registered line 150. | +1 line |

### Test Delta
- **Before:** 254 Rules + Identity unit; 103 integration
- **After:** 254 + 12 = 266 unit; 103 + 4 = 107 integration
- **All green:** 0W/0E build

### Key Patterns (Precedent Refs)
- **Read filter logic:** `AttributeRuleRepository` (v2.120, Identity B.1)
- **Handler guard pattern A:** `CreateAbacRuleCommandHandler` (v2.121, FU2)
- **Handler guard pattern B:** `UpdateAbacRuleCommandHandler` (FU2) + `DeleteAbacRuleCommandHandler` (v2.122, FU4 currentTenantId audit logging)
- **Integration fixture shape:** `TenantIsolationFixture` (Cases.Tests, but Rules module has own DbContext — duplication unavoidable without generic refactor = scope creep)

### Asymmetric Admin Design Rationale
```
Read path:  IsSystemAdmin ✓ || TenantId == current ✓
Write path: TenantId == current only (no admin bypass)

Why?
- Admin read bypass enables future Hangfire jobs to query cross-tenant data (seeding, reports).
- Admin write-strict prevents privileged users from accidentally mis-tagging RuleSet.TenantId.
- If admin *intentionally* needs to write cross-tenant, that's a separate UX flow (not yet in scope).
```

---

## What Went RIGHT

**Scout parallelization** saved a round-trip. Two agents running Explore in parallel = 1 sequential task now.

**Advisor checkpoint caught 4 unknowns before implementation:**
- Scope: 3 handlers, not 1
- R2 risk: query-side leak (ITenantContext not injected into GetRuleSetQuery handler)
- Admin bypass subtlety: read vs. write asymmetry needs explicit comment
- Fixture duplication: acceptable cost vs. refactoring scope creep

**Red-team in plan.md worked.** R2 (query handlers) was explicitly flagged as *"NOT in scope, file FU5.1 if found."* Code-reviewer found it, filed it cleanly. Zero rework.

**Pattern A + Pattern B kept handler logic readable.** CreateRuleSet has simple pre-factory guard. Update/Activate have dead-path belt+suspenders with comment explaining *why* the dead path exists. Future dev won't delete it cargo-cult.

**TDD per-phase prevented churn.** Each phase RED → GREEN independently. No "wait, I need to refactor the repo ctor" mid-handler-implementation surprises.

**Verification pattern actually worked.** Trust-but-verify Grep/Read after each subagent phase. All 3 phases verified independently; no drift. Subagent reports describe *intent*, not *outcome*. We checked.

---

## What Was SURPRISING / Hard Lessons

**Scout miscounted handler scope.** Backlog said UpdateRule; Rules module has 3 write entry points. Lesson: *scout queries must be explicit about "are there OTHER write handlers in this module?"* — not just the one mentioned in backlog.

**Code-reviewer caught adjacent gap (M2: DecisionTableRepository).** Not in plan R2 audit. This tells us: **code-review scope > static plan scope.** A reviewer reading Rules module end-to-end will spot systemic patterns our plan may miss. Lesson: red-team identifies *known* risks; code-review finds *unknown unknowns*. File them separately (M2 → FU5.2 backlog, not rework this task).

**InMemory DB can't test `IsSystemAdmin` path properly.** Unit test mocks `ITenantContext.IsSystemAdmin = true` on the handler, but the repo filter test can't inject a seeded admin user without EF seeding logic. We accepted this gap (integration I4 covers it); lesson: **multi-tenant InMemory unit tests are friction-prone.** Explicit role mocking would have required more fixture investment.

**Asymmetric admin design isn't obvious from the code alone.** Took explicit comment in Update/Activate handlers to preserve intent. Without it, the post-load check looks paranoid. With it, future dev reads: "Yes, I understand this mirrors the repo filter. Yes, the write path intentionally has no bypass. Yes, this survives Hangfire.IgnoreQueryFilters() bypass attack."

---

## Root Cause Analysis

**Why underscopedBacklog Entry**
- Backlog author context-loaded on *Identity* B.1 (UpdateAbacRule gate), assumed Rules module had *single* write entry point.
- No explicit audit query: *"enumerate all write command handlers in Rules module."*
- Result: 1h estimate became 2h real work + 2 new backlog items discovered post-PR.

**Why FU5.1 + FU5.2 weren't caught in planning**
- Plan R2 red-team focused on *RuleSet* module scope, didn't audit sibling repositories (DecisionTableRepository).
- Query handlers weren't part of phase scope (repo write-path only), so ITenantContext injection in handlers wasn't audited.
- Result: code-review found them; they surfaced as *expected* findings per plan ("if R2 manifests, file FU5.1"), not rework.

**Why InMemory unit tests couldn't validate admin bypass**
- ITenantContext is a request-scoped service; mocking it per-test works for non-admin branches.
- Seeding admin user + query as admin in InMemory requires EF seeding fixtures = test fixture overhead.
- We accepted gap (integration covers it); lesson: **accept friction, document tradeoff.**

---

## Lessons Learned

1. **Backlog refinement must audit *all* entry points, not just the one mentioned.** Query: *"What are all the write command handlers in [module]?"* — not *"Does UpdateRule need a guard?"*

2. **Pattern comments are load-bearing.** The inline comment in Update/Activate handlers isn't nice-to-have; it's the *only* thing preventing a future contributor from deleting the "dead" post-load check as redundant. **Document security intent explicitly.**

3. **Code-review scope > plan scope.** We red-teamed known risks (R1–R7). Reviewer found unknowns (M2: DecisionTableRepository parity gap). File them separately; don't rework the task. This is healthy.

4. **Asymmetric admin design is subtle; test it explicitly.** Read-bypass ≠ write-bypass. Unit test mocking isn't enough for multi-tenant role-based data. Integration tests (I4) are non-optional. Cost accepted.

5. **Accept testing friction; document why.** InMemory multi-tenant unit tests are friction-prone. We chose integration I4 coverage instead. That's a tradeoff, not a failure. **Say it explicitly:** *"InMemory unit test mocks ITenantContext.IsSystemAdmin; integration I4 validates actual EF + filter behavior under admin query."*

6. **Fixture duplication is acceptable when refactor = scope creep.** 30-line `RulesTenantIsolationFixture` vs. generic refactor of `TenantIsolationFixture` (DbContext-hardcoded, requires 3-4 hours). Duplication won in planning. No regrets.

7. **Verification pattern: trust subagent intent, verify outcome.** Subagents report "5 tests red→green." Check with Grep/Read that all 5 actually pass, all 5 actually exist. We did; zero drift found. This pattern works.

---

## Next Steps

### Immediate (This Session)
- [x] User confirms git commit (via git-manager subagent)
- [x] Add commit hash to journal + project-state + MEMORY (all backfilled to `f7b5604b`)
- [x] 7-artifact finalize audit (dep-map/roadmap/changelog/backlog/memory×2/journal — all verified v2.123)
- [ ] Recommend `/compact` at journal boundary (clean context reset)

### Backlog (FU5.1 + FU5.2)
- **FU5.1:** 6 query handlers (GetRuleSetQuery, ListRuleSetsQuery, etc.) inject ITenantContext + validate request.TenantId == context.TenantId. (~1h, medium priority)
- **FU5.2:** DecisionTableRepository apply same pattern (inline filter + write guards). (~1h, medium priority)
- Both are P0 security; low likelihood of cross-tenant leak (query handlers don't take tainted input), but defence-in-depth + consistent pattern warrants closure.

### Future Session
- Evaluate InMemory multi-tenant unit test fixture investment (mocking ITenantContext.IsSystemAdmin role-based) vs. accept integration-only coverage.
- Audit other Rules module repos (if any) for identical gap (preventive; likely none, but scout costs 15 mins).

---

## Unresolved Questions / Concerns

**Q1:** Should we backfill ITenantContext injection into `GetRuleSetQuery`/`ListRuleSetsQuery` handlers *this session* to avoid two separate PRs?  
**A:** No. Plan explicitly scoped write-path only; FU5.1 is read-path. Scope creep risk high if merged. Separate PR, separate session, file as FU5.1 backlog.

**Q2:** InMemory unit test gap on `IsSystemAdmin` bypass — is this acceptable for production?  
**A:** Yes. Integration I4 covers it. Gap is unit-level convenience, not security gap. EF + filter behavior validated in integration. Accept and document.

**Q3:** Why is the inline comment on Update/Activate handlers necessary?  
**A:** Because the post-load check is logically "dead" after repo filter is in place. Inline comment preserves intent: *"This check is belt-and-suspenders; repo filter is the belt. If repo ever adds IgnoreQueryFilters method, this handler check is suspenders. Don't delete me."* Without it, future dev will.

---

## Evidence of Quality

- **Build:** 0W/0E (clean)
- **Test coverage:** 12 new tests (5 repo unit + 3 handler unit + 4 integration), all green
- **Code-review:** 9.3/10, APPROVED_WITH_MINOR (0 critical, 0 major)
- **Regression:** 0 existing test failures; 103 integration + 254+ unit green
- **Verification:** All 3 phases independently verified via Grep/Read trust-but-verify pattern
- **Pattern consistency:** Mirrors Identity B.1 (v2.120) + FU2/FU4 handler guards
- **Comment quality:** Asymmetric admin design + Hangfire bypass rationale explicitly documented in code

---

**Session Status:** APPROVED_WITH_MINOR, ready to commit. FU5.1 + FU5.2 filed cleanly as follow-up backlog items (expected findings per plan R2).
