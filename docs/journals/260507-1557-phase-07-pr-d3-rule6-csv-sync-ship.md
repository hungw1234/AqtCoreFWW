# Phase-07 PR-D3 — Rule6 residual CSV sync (MERGED)

**Date:** 2026-05-07 15:57 (+07), merged 16:28 (+07)
**Branch:** `feat/phase-07-pr-d3-rule6-csv-sync` (deleted post-merge)
**PR:** [#84](https://github.com/thiennd212/AqtCoreFW/pull/84) MERGED 2026-05-07T09:28:59Z
**Squash commit on main:** `0f0aa2b5`
**Pre-squash commit:** `73ace591`
**Code review:** APPROVE 9.5/10 (`plans/reports/code-reviewer-260507-1557-phase-07-pr-d3-rule6-csv-sync.md`)

## What

3 rows in `plans/260503-0045-foundation-perf-surgery/research/entity-tier-mapping-data.csv` flipped T1→T2:

| Module | Entity | External addressing |
|--------|--------|---------------------|
| ai | AiModelProfile | `/api/v1/ai/model-profiles/{publicId:guid}` |
| ai | AiPromptTemplate | `/api/v1/ai/prompt-templates/{publicId:guid}` |
| identity | MfaPolicy | `/api/v1/admin/security/mfa-policies/{publicId:guid}` (QĐ742 1.6.a) |

Source code untouched — all 3 entities ALREADY implement `IPublicIdentified` + factory-issued `Guid PublicId`. CSV registry was stale "T1 internal-only" since Phase-01. PR exact mirror of PR-C Partner+FormTemplate sync (`5a70062f`).

## Why

`Rule6_T1_Entities_Have_Long_Id_And_No_PublicId` (architecture suite) had been red-with-1-baseline since Phase-04a B5 ship. PR-D2 memory called it out as "PR-D3 residual." Two viable fixes per entity: (a) retier T1→T2, or (b) remove `IPublicIdentified`. (a) wins trivially — controllers + DTOs + repos already designed around `PublicId`; option (b) would cascade-break admin UI.

## Test delta

| Suite | Before | After |
|-------|--------|-------|
| `AqtCoreFW.SharedKernel.Tests` Architecture trait | 26/27 PASS (1 baseline = Rule6) | **27/27 PASS** |
| `AqtCoreFW.Architecture.Tests` (module-wide IL) | 26/26 PASS | **26/26 PASS** (no regression) |
| Source: `long {Ai*Id\|MfaPolicyId}` on Application surface | n/a | **0 hits** (Rule_T2 zero-leak verified) |

## Phase-07 status post-PR-D3

PR-A (#76) → PR-B-pre (#78) → PR-A-Forms (#79) → PR-B (#80) → PR-C (#81) → PR-D1 (#82) → PR-D2 (#83 cherry-picked → `74f7ac23`) → **PR-D3 (#84)**.

Phase-07 retier wave + Rule6 cleanup **complete**. `T2FkDriftBacklog` HashSet stays empty (PR-D2 drained, PR-D3 added 0). Rule_T2 enforces zero exemptions on all 3 newly-flipped T2 entities going forward.

## Process notes

1. **Trivial-task ceremony tradeoff.** Plan was a 3-line CSV diff; spawned `code-reviewer` + `docs-manager` + `git-manager` per /ck:cook MANDATORY. Cost ~3 sub-agent calls for a 1-minute edit. Senior-mode tradeoff acceptable: review caught zero issues but provided independent confirmation of zero-leak scan.
2. **`git-manager` false-report recurrence.** Reported CSV "excluded as gitignored." Actual: CSV tracked (verified `git ls-files --error-unmatch` + commit log shows `73ace591` includes it). Same misreporting class as memory `feedback-git-manager-merge-verify.md`; not retreating — verified durable git state via `gh pr view --files` before declaring done.
3. **Backlog spawn (non-blocking, unchanged from prior memory).** `T2EntityRegistry` record field labels (`Rationale, Exposure`) drift from CSV header (`external_addressing_key, rationale`). Rules read only `Tier`/`Entity`, so harmless — but rename record fields next opportunity.

## Files changed (5)

- `plans/260503-0045-foundation-perf-surgery/research/entity-tier-mapping-data.csv` (+3/-3)
- `docs/codebase-summary.md` (+1/-1)
- `docs/dependency-map.md` (+1/-1)
- `docs/development-roadmap.md` (+2/-2)
- `docs/project-changelog.md` (+29/-0)
