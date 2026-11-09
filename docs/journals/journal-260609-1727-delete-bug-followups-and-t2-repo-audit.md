# Journal — 2026-06-09 17:27 · Delete-bug follow-ups closed + T2 repo find-by-PublicId audit

## Session goal
Close the loop on the 3 follow-ups the WF definition delete bug (`adde5afc`) spun off, via two `/cook` slices:
1. `/cook "2 fix test-only"` — `WFE-DEFS-SPEC-STALE-SAVE` + `WF-CREATE-RULE-HANDLERS-COVERAGE` (re-entry-light after the prior wrap).
2. `/cook WF-T2-REPO-FIND-AUDIT` — audit the delete bug's root-cause pattern across the codebase.

## What worked (verified + commit refs)
- **`WFE-DEFS-SPEC-STALE-SAVE`** — `workflow-definitions.spec.ts` 9/9 green vs live stack (`1adcd5ea`). Unblocked 6 dormant downstream tests.
- **`WF-CREATE-RULE-HANDLERS-COVERAGE`** — new `WorkflowCreateRuleHandlersDefinitionResolutionTests` 3/3 real-SQL (`1adcd5ea`). Guards the 3 create-rule handlers the delete fix un-broke (PublicId→long resolution). HTTP for assignment/notification; handler-direct for escalation (no create endpoint).
- **`WFE-DYNAMIC-INSTANCES-SPEC-STALE`** (sibling found en route, user opted fix-now) — `workflow-dynamic-instances.spec.ts` 10/10 green (`1adcd5ea`).
- **`WF-T2-REPO-FIND-AUDIT`** — audit + 2 defensive `DeleteAsync(Guid)` overrides (WorkflowInstance + FormSubmission) + 2 integ tests 2/2 (`3f66351f`/`44a0f07a`).
- All pushed origin+bitbucket, ahead=0. build 0W/0E throughout · all code-reviews APPROVE 0C/0H.

## What didn't / lessons
- **Backlog "Root cause (likely)" was WRONG (INVARIANT 260522-A, reinforced ×3 this session).** `WFE-DEFS` row claimed the 422 was a missing assignment; the real 422 (captured via temp `GRAPH_DEBUG` console.log) was `GOV_SYS_002` non-canonical state `InReview` (canonical `Generic` vocab uses `UnderReview`). Both fixes were needed, but the named one was not the surfaced error. → Capture the actual error body before trusting a row's diagnosis.
- **A long-dormant e2e spec hides LAYERED rot.** `workflow-dynamic-instances` had never run past `setup` (always 422) → 4 independent staleness defects, each exposed only after fixing the prior (vocab → assignment → fixed-entityId-409 → wrong create-task field name). Budget for N>1 fixes; capture each real body.
- **Playwright `list` reporter BUFFERS `console.log`** — had to redirect run output to a file + grep (cleaned of ANSI) to read debug captures.
- **Time sink:** bringing up the full dev stack (Auth+API+FE) for the API-contract e2e — globalSetup probes ALL three hosts (incl. FE:5173) even for request-only specs.
- **Audit overestimation:** the `WF-T2` row feared "audit ALL T2 repos ~2-3h"; the real surface was 3 repos, get-by-id already safe on all, the only gap unreachable. Enumerating the EXACT surface (grep + DI registrations) beat assuming breadth.

## Decisions made
- **`WF-T2` defensive vs YAGNI (borderline → AskUserQuestion):** the `DeleteAsync` gap is latent (0 call sites; these entities are never hard-deleted) — presented 3 options (YAGNI-close / defensive overrides / base fail-fast guard). User chose defensive overrides. Rationale: cheap insurance against the exact bug class that just cost a prod bug; mirrors the proven WorkflowDefinitionRepository override.
- **`WFE-DYNAMIC` fix-now vs defer:** user opted to fix the sibling spec in-session rather than carry it (scope-guarded: verified empirically RED first, then asked).
- **DI containment finding:** no open/closed-generic `IRepository<T2,Guid>` registration → the broken base is never injectable; the specific-interface override is the only path. The registration shape is the real safety net.

## Test status
- Integration: `WorkflowCreateRuleHandlersDefinitionResolutionTests` 3/3 · `WorkflowInstanceRepositoryDeleteByPublicIdTests` 1/1 · `FormSubmissionRepositoryDeleteByPublicIdTests` 1/1.
- e2e (live stack): `workflow-definitions` 9/9 · `workflow-dynamic-instances` 10/10.
- Builds 0W/0E. Code-reviews APPROVE 0C/0H (one Info-tier doc-wording softened; one cosmetic note declined).

## Next session
- `WF-T2` optional base fail-fast guard (option C — not taken; would prevent the whole class for future T2 repos).
- `WFE-RATELIMIT-TEST-DISABLE` (harness 429 flake — root cause unresolved).
- **Open-Q:** do e2e specs run in CI? If yes, `workflow-definitions` + `workflow-dynamic-instances` were silently red until this session.
- Higher-value backlog candidates remain (`R4-EXEC` stack-gated; `WF-TRANSITION-NOTIFY-UNCONFIGURED` needs /plan).
