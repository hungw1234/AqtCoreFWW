# Journal — 2026-07-09 17:16 · Forms SetCode public-Code 409 guard

**Row:** `PUBLIC-FORMS-SETCODE-PUBLIC-RENAME-409` (T4 Low) · **Commits:** `b063f27d` (fix) · `8ac7b60f` (docs-close) · `e5b4358c` (docs-file-FU) — all pushed origin/main.

## Session goal
Close the last open follow-up of the public-forms global-uniqueness arc: the friendly 409 for a colliding public Code lived only in the IsPublic-toggle handler; the SetCode (Draft rename) path still surfaced a raw 500 from the `UX_FormTemplate_PublicCode` filtered unique index.

## What worked
- Mirrored the sibling `SetFormTemplatePublicAccessCommandHandler` guard onto `UpdateFormTemplateCodeCommandHandler`: `public_forms_bypass` Dapper probe on `request.NewCode` → 409 `FormErrors.PublicCodeConflict` (GOV_FRM_019 reused, no new code).
- INVARIANT 260522-A audit up-front confirmed the gap empirically (handler checks only per-tenant `GetByCodeAsync`; `SetPublicAccess` has no Status precondition so Draft+IsPublic=1 is reachable).
- Verified: build Release `/warnaserror` 0W/0E · Forms.Application.Tests 344/344 · TenantIsolation PublicFormsLeastPrivilegeTests 9/9 · code-review 0 Crit/High.

## What didn't / lessons
- **L1 (Draft-gate):** first cut gated the probe on `IsPublic` alone → an Active+public rename would return 409 instead of `GOV_FRM_010`, firing where no index-500 is reachable (SetCode is Draft-only → throws before any UPDATE). Fixed to `IsPublic && Status==Draft`. Lesson: an app-guard mirroring a DB constraint must gate on AND(constraint filter predicates)AND(mutation precondition).
- **M1 (test the delta):** unit tests first used `Arg.Any` for the probe param → the one behaviour separating this from the sibling (probe `NewCode`, not `template.Code`) was unasserted; a copy-paste bug would pass green. Fixed with a reflection matcher pinning `Code==NewCode` & `ExcludeId==template.Id`.
- Both caught by the mandatory code-reviewer, not by me — reinforces running it even on "small" mirrored changes.

## Decisions made
- **L2 dead-code, shipped anyway (user gate):** verified repo-wide that `UpdateFormTemplateCodeCommand` has no endpoint / MediatR dispatch / FE consumer — the guard is correct-when-wired insurance with no user-facing payoff today. User chose "Commit + close row" over WONTFIX-revert (arc-symmetric, zero-risk).
- **Followed through** (user "handle #1/#2"): filed FU `FORMS-CODE-RENAME-ENDPOINT-FE-WIRING` (T4, blocked-on product-Q) with concrete wiring spec (mirror `.../public-access` PATCH), then pushed all 3 commits (verified `0 0` in-sync).
- **Test depth:** unit (handler wiring/mapping/gating) + one integration Test 9 (Code-UPDATE index collision, non-redundant vs Test 7 IsPublic-flip). Skipped a full-handler-under-least-priv integration test — the probe SQL predicate is already covered by the existing Test 8.

## Test status
Build 0W/0E (Release, warnaserror). Forms.Application.Tests 344/344. TenantIsolation PublicFormsLeastPriv 9/9. No regression (only the new test constructs the handler; DI is assembly-scan).

## Next session
- **Product-Q (unblocks FU):** is Draft Code-rename a needed user capability? Code is set only at create today. If yes → wire endpoint + FE (~0.3–0.5d) per `FORMS-CODE-RENAME-ENDPOINT-FE-WIRING`.
- Auto-memory index (`~/.claude/.../MEMORY.md`) ~21KB — needs a dedicated compaction/archive pass.
