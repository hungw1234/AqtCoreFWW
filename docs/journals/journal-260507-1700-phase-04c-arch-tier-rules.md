# Phase-04C Tier-Rule Architecture Tests — Journal

**Date:** 2026-05-07 17:00 ICT
**Branch:** main (uncommitted, awaiting code-reviewer + git-manager)
**Plan:** `plans/260507-1652-phase-04c-arch-tier-rules/`
**Backlog:** PHASE4C-ARCH (P1) → DONE
**Test delta:** Architecture suite **27 → 30 PASS** (+R_INTERNAL_CTRL +R_T2_REPO_SIG +CoverageFact)

---

## What changed

Added one new test file — `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/TierRepoArchitectureRulesTests.cs` (~250 LOC) — hosting 3 forward-looking architectural guards.

| Rule | Type | Drift it prevents |
|------|------|-------------------|
| `R_INTERNAL_CTRL` | source-level scan | Controllers calling repo `*InternalAsync` methods → leak long internal Id past HTTP boundary |
| `R_T2_REPO_SIG` | source-level scan + CSV-driven tier filter | T2 repo interfaces declaring single-overload `GetByIdAsync(long)` → ambiguity vs PublicId AK convention |
| `CoverageFact_T2_Entities_Have_Discoverable_Repository_File` | filesystem coverage | Naming drift hides T2 entities from R_T2_REPO_SIG scan |

All 3 rules ship with **0 violations** on the current codebase. Pure forward-looking tripwires for Phase-4b sibling work (`GetByIdInternalAsync(long)`).

---

## Why now

Backlog item PHASE4C-ARCH (P1, blocks Phase-08 finalize gate) said "Plan said 'B7 un-skip' but tests don't exist yet — needs WRITING". Re-grep confirmed half the items already shipped: R1 (Guid.NewGuid ban) + R_T2FK (DTO long-Id leak ban) exist in `SourceLevelRulesTests.cs`. Real gap: only 2 rules + 1 coverage fact missing. PR-C convention (`5a70062f` deleted `IPartnerRepository.GetByIdAsync(long)` single overload) was unenforced; without a rule, the next refactor could regress silently.

---

## Key design decisions

### 1. New file, not append
`SourceLevelRulesTests.cs` is 165 LOC. Adding 3 facts (~80 LOC each) would push it past 200 LOC. Per `CLAUDE.md` modularization rule, split into a dedicated file. PascalCase per C# convention.

### 2. Entity-aware filter via CSV tier registry
14 existing `GetByIdAsync(long)` declarations are ALL T1 (legitimate — long IS the addressing key for T1). Bare ban → 14 false-positives. CSV-driven filter (only fail T2) → 0 violations. Rule reads `T2EntityRegistry.Load().T2()` and filters by entity name derived from `I{Entity}Repository.cs` filename.

### 3. ApplicationRole exception (mirror existing list)
ApplicationRole is CSV T2 but uses `IRoleRepository.GetByIdAsync(Guid)` (OpenIddict Guid-PK lock). Mirrored existing `AspNetIdentityGuidPkExceptions` HashSet (8 entities) from `IdentifierTierShapeRulesTests.cs:50`. Skip these in R_T2_REPO_SIG.

### 4. NO suppress directive on R_T2_REPO_SIG (KISS)
0 expected violations. SourceCodeScanner's entityExtractor signature is line-only (`Func<string, string?>`); entity-aware suppress would need filename context, requiring scanner API extension. Deferred until Phase-4b legitimate edge case arises (~1h refactor when needed). Documented trade-off in fact's doc comment.

### 5. R_INTERNAL_CTRL DOES carry suppress directive
Method-name suffix `*InternalAsync` is line-extractable; suppress works without scanner changes. Grammar: `// arch:suppress R_INTERNAL_CTRL <MethodName>: <reason>`. Adapted from R_T2FK's entity-based pattern (entity = method name spelled exactly).

### 6. Coverage fact has 3 maintenance lists
- `AspNetIdentityGuidPkExceptions` (8) — Identity Guid-PK satellites
- `T2NoRepoExceptions` (11) — T2 entities without dedicated repo (DbContext-direct: 5 masterdata + 2 identity + 1 organization; aggregate-owned: Rule)
- `T2RenamedRepoMap` (5) — T2 entities whose repo file uses non-conventional name (FileRecord→IFileRepository, etc.)

Maintenance protocol documented inline. Future T2 retier triggers explicit decision: add repo OR add to exception list.

---

## Verification trail

- Build 0E/0W (`dotnet build tests/unit/AqtCoreFW.SharedKernel.Tests/`)
- Suite 30/30 PASS (`dotnet test --filter Category=Architecture`)
- **Negative test #1:** injected `__ArchProbeController.cs` containing `await GetByIdInternalAsync(123L)`. Re-ran R_INTERNAL_CTRL → FIRED with clear violation message. Probe deleted.
- **Negative test #2:** injected `Task<Notification?> GetByIdAsync(long id, ...)` line into `INotificationRepository.cs` (T2). Re-ran R_T2_REPO_SIG → FIRED with `[entity=Notification]` in violation message. Reverted.
- **T1 immunity:** 14 existing T1 `GetByIdAsync(long)` declarations stayed silent (suite green proves it).
- `git status --short`: only one new untracked file (the test file) — clean revert verified.

---

## Files changed

**Added:**
- `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/TierRepoArchitectureRulesTests.cs`
- `plans/260507-1652-phase-04c-arch-tier-rules/plan.md` + 4 phase files
- `plans/260507-1652-phase-04c-arch-tier-rules/research/violation-baseline-260507.md`
- `plans/260507-1652-phase-04c-arch-tier-rules/research/t2-repo-coverage-260507.md`
- `docs/journals/journal-260507-1700-phase-04c-arch-tier-rules.md` (this file)

**Modified:**
- `docs/dependency-map.md` — header version bump v2.136-P7-PR-D3 → v2.136-P4C-ARCH-TIER-RULES, suite count 27/27 → 30/30
- `backlogs/backlog.md` — PHASE4C-ARCH removed from active list
- `backlogs/completed.md` — PHASE4C-ARCH appended

---

## Decisions deferred to future PRs

1. **Suppress directive on R_T2_REPO_SIG** — extend SourceCodeScanner's `entityExtractor` to accept `(filePath, line)` tuple. Wire when first legitimate Phase-4b edge case surfaces. ~1h.
2. **R_T2FK extension to controllers + ModuleClients** — declined as YAGNI per Q3 user vote (Application surface sufficient given ThinController + ModuleClient convention guards).
3. **Permanent `[Theory]` negative tests** — declined as YAGNI per Q2 user vote (one-shot manual verification at implementation time mirrors existing pattern in suite).

---

## Unresolved questions

None. All 4 planner-raised questions resolved before implementation:
- Q1 (suppress on R_T2_REPO_SIG) → No
- Q2 (negative-test pattern) → One-shot manual
- Q3 (R_T2FK extension scope) → Defer
- Q4 (T2 exception list) → resolved in Phase-01 research

---

## Phase-08 finalize gate

PHASE4C-ARCH was the last gating item before Phase-08 finalize gate per backlog. With 30/30 PASS and these guards in place, Phase-08 finalize is unblocked.

---

## Addendum: Emotional truth & learning

**What was easy:** This task was *unusually friction-free*. The plan was tight, the CSV tier registry already existed, and the 3 rules felt natural once the shape was clear. No false-starts, no architecture reversals.

**What made it work:** The heavy lifting happened in Phase-04B (tier classification finalized). Phase-04C just *locked it down* via tests. This is why splitting multi-phase architectural work matters — by the time you write the guard, the design is already proven.

**The insight:** The real value isn't the 3 rules themselves (they're simple scans). It's the *maintenance lists* — AspNetIdentityGuidPkExceptions, T2NoRepoExceptions, T2RenamedRepoMap. These forced explicit decisions about exceptions **at rule-creation time**, not deferred until Phase-4b crashes. One table beats 10 future debugging sessions.

**Deferred pattern worked:** Decided to NOT extend SourceCodeScanner for R_T2_REPO_SIG suppress (decision #4). This was a deliberate trade-off: accept Phase-4b might need 1h refactor vs. pre-emptive API expansion. Phase-4b will tell us if this was right. Current rule coverage (14 T1 immunity + 8 Guid exceptions) should handle 90% of cases.

**Truth:** Zero struggle here. But that's not bad — it means the Phase-04 architecture (tiers, tier registry, CSV sync, naming conventions) is **solid enough to enforce**. The tests are just paperwork on a foundation that already works.
