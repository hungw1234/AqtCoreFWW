# Phase-04a Seeder Audit Closure (P1)

**Date**: 2026-05-08 19:44Z
**Severity**: Medium
**Component**: Seeding infrastructure (Organization module)
**Status**: Resolved

## What Happened

Backlog item "PHASE4A-SEEDER-AUDIT scope expanded" scoped to ≤15 fixes. Scout pass across 11 active seeders + 5 Dapper callsites returned 1 DRIFT-CONFIRMED: `BtcAssignmentSeeder.UserRow` (line 184) using positional record with nullable params — exact anti-pattern identified in PR #75 + memory rule.

**Fix**: Converted `private sealed record UserRow(Guid Id, Guid? PrimaryOrgUnitId, string? PositionName)` → `private sealed class UserRow { public Guid Id { get; init; } public Guid? PrimaryOrgUnitId { get; init; } public string? PositionName { get; init; } }` (commit `6a5772a2`).

## The Brutal Truth

Backlog premise ("scope expanded, multiple residuals expected") **refuted**. PR #75 had already closed this class of defect systematically. Lesson: **aging backlog items deserve scoped re-verification before blind expansion**. Trust-but-verify rule (memory `feedback-verify-subagent-reports`) caught this — Scout hedge-labeled the file "likely safe" but main agent re-read the source + template reference and confirmed drift. Without verify-pass, audit closes false-green again.

## Technical Details

- File: `src/modules/organization/AqtCoreFW.Organization.Infrastructure/Seeding/BtcAssignmentSeeder.cs:184`
- Root cause: Positional record with 3 nullable params lacks parameterless ctor → Dapper silently skips field materialization on null input
- Tests pass: Architecture 26/26, Organization unit 128/128, Smoke 1/1 all GREEN
- Pre-existing failure (unrelated): Organization integration 12/12 FAIL (`BtcOrphanPurgerTests` SQL type clash in helpers)

## What We Tried

1. Scout scanned 11 seeders + pattern-match on `new` + `IDataReader` callsites → 1 hit
2. Verified against PR #75 precedent + template at `BtcIdentitySeeder.Users:362-371` (already corrected)
3. Promoted memory rule → `docs/code-standards.md` lines 272–286 (sealed class + init-only properties standard)

## Root Cause Analysis

**Backlog drift + confidence collapse.** Backlog created ~38 days ago when PR #75 landed. Subsequent pattern recurrences (e.g., PHASE7-FE-DTO-CONTRACT-CLASSIFY same shape — premise refuted on per-field audit) suggest aging backlog items are estimates, not audits. Lack of explicit "verify-pass" gate allows false negatives to persist across phases.

## Lessons Learned

1. **Verify-pass before closing audits.** Scout reports = intent, not ground truth. Re-read ≥1 file yourself when trust is low.
2. **Backlog atrophy discipline.** 38-day-old "scope expanded" item should trigger re-scoping conversation, not blind expansion. Scope cap (≤15 fixes) was correct constraint.
3. **Documentation promotion = regression guard.** Memory rule now in `docs/code-standards.md` (code-standards readers will find it; memory-only rules decay). Future seeders/handlers reference the doc.

## Next Steps

- **Backlog spawned 3 P3 FUs** (commit `e6f2d930`):
  - FU1: ProvinceSeeder + OpenIddictDataSeeder static-class state isolation
  - FU2: Pre-emptive `ValueGeneratedNever()` grep regression watch
  - FU3: Architecture rule banning `record \w+\(.*\?` in seeder folders (mechanical guard, 3rd memory hit on this pattern)
- Branch `feature/phase-04a-seeder-audit` ready for merge (PR pending)

**Status:** DONE
