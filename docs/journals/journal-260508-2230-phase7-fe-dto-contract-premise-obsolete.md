# PHASE7-FE-DTO-CONTRACT — Premise obsolete, closed without code change

**Date:** 2026-05-08 22:30 ICT
**Branch:** `main` (no code change)
**Backlog rows closed:** PHASE7-FE-DTO-CONTRACT (P1, est 8-16h), PHASE7-FE-WF-DTO (P1)

## TL;DR

Cook attempt on largest open P1 surfaced empirically obsolete premise. Source verify (~10min greps) confirmed all 19 listed drift surfaces closed by 5 prior ships across 2026-05-06 → 2026-05-08. Closed both rows in `backlogs/backlog.md` + archived to `completed.md`. Zero LOC changed.

## What happened

User invoked `/cook PHASE7-FE-DTO-CONTRACT --auto`. Per advisor pattern + senior-engineer mode rules ("MUST flag decisions that need team discussion"), instead of rubber-stamping an 8-16h workflow, ran source-level verification first:

1. `MfaPolicyDto.cs` → already T2 with `Guid Id` (line 10, mapped from `entity.PublicId`)
2. `tests/unit/AqtCoreFW.SharedKernel.Tests/Architecture/SourceLevelRulesTests.cs:87` → `T2FkDriftBacklog = new(StringComparer.Ordinal)` (empty HashSet, drained by PR-D2)
3. `MfaPoliciesAdminController.cs:35,51` → `ApiResponse<Guid>` envelope + PUT route `{publicId:guid}`
4. Memory cross-check: PR-D2 (`a903fb35`) drained T2 FK leaks; PR-D3 (`0f0aa2b5`) closed Group A admin DTOs; PR #86 CLASSIFY shipped 22 documentary T1 directives; PR #89 (`60eba78e`) FE Tier B/C/D PASSED — proving contract layer is FE-functional.

Remaining `long {Name}Id` hits in Application surface (`SessionId`/`MessageId`/`FieldId`/`TransitionId`/`TemplateId`) belong to T1 entities — intentional per CLASSIFY ship. R_T2FK rule does NOT fire on them (T1 not in T2 enforcement list).

## Decision

Option A (close backlog row, ~10min docs sweep) over Option B (run smoke tests first) because:

- Source-level grep evidence was conclusive
- Architecture test re-run blocked by file lock (running `AqtCoreFW.Api` PID 7208 — `feedback-kill-dotnet-before-build` memory; not worth killing user's running app for redundant proof)
- Risk of A: false negative (missed real drift) — mitigated by 8-row evidence table in completed.md showing each sub-item's resolution ship + file path

## Files changed

- `backlogs/backlog.md` — 2 rows: PHASE7-FE-DTO-CONTRACT + PHASE7-FE-WF-DTO marked RESOLVED with single-line rationale + ship pointers
- `backlogs/completed.md` — prepended closure entry with full 8-row evidence table

## Lessons

- **Pre-cook source verify is cheap.** ~10min of targeted greps + reads avoided 8-16h of fictitious refactor work. Hard-gate "plan before code" + advisor pattern paid off.
- **Backlog drift is real.** Row written 2026-05-06 became obsolete by 2026-05-08 across 5 ships (PR-D2/D3/#86/#76/#89 + READ-SIDE adapter fix) — none of those PRs explicitly cross-referenced PHASE7-FE-DTO-CONTRACT row. Closing rows during cook session is a maintenance backstop.
- **Memory recall guidance validated.** Memory `Before recommending from memory: A memory that names a specific function, file, or flag is a claim that it existed when the memory was written` — exactly the failure mode caught here.

## Unresolved questions

None. Backlog state is now consistent with `main` HEAD `8e0e34d9`.
