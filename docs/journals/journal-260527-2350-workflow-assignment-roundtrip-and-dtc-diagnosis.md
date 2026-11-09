# Journal — Workflow assignment round-trip fixes + BP-run DTC diagnosis (2026-05-27 evening)

## Session goal
User-reported live-bug triage on the workflow designer + BP-run flow (dev stack running). 5 distinct issues surfaced sequentially via `/ck-debug` + `/fix`.

## What worked (verified + committed)
- **`7789de3a` fix(workflow): project transition assignment in GetWorkflowDefinition query** — read-path gap. Save persisted `WorkflowTransitionAssignments`+`Clauses` and the save *response* projected them from the in-memory spec, but the Dapper GET query never read those tables → clauses vanished on reload (+ flashed away post-save via `useSaveDefinitionGraph` cache-invalidation → refetch). Added 2-query read-back + in-memory join, mirroring `WorkflowDefinitionRepository.GetOutgoingWithAssignmentsAsync`. Empirically confirmed write-side OK via SQL probe (`assignments=5, clauses=5`) before fixing. Regression integration test drives save→GET (NOT save response) — string-enum payload, 2 clauses, asserts enum-string values + ORDER BY + refsJson null. WorkflowDefinitionsApiTests 9/9.
- **`921d01f5` fix(workflow-ui): properties sidebar validation + apply feedback** — `handleSave` swallowed `validateFields()` rejection (no try/catch) → invalid required field scrolled out of the 360px drawer made the Apply button look dead; success gave no feedback. Added `.catch`→warning toast, `scrollToFirstError`, success toast clarifying Apply=local / Lưu=persist. Root cause of "Áp dụng không hoạt động" was a mental-model mismatch (Apply is local-only by design) + zero feedback.
- **`88be845a` fix(workflow-ui): string enum members for assignment clause modes** — combobox showed raw `"Initiator"` not "Người khởi tạo". `AssignmentClauseMode` was typed `0|1|...|6` (the ONLY numeric enum union in the FE) but the API serialises enums as strings (`JsonStringEnumConverter`). Latent until the read-back fix first fed server data into the editor. Switched to string unions matching the wire — house convention (every other FE enum is a string union). 3 files, FE typecheck/unit 8-8/lint clean.
- **typecheck:test 26→0 (UNCOMMITTED, 4 files)** via `/fix` — node types missing in `tsconfig.test.json` (e2e helper pulled in transitively), Vitest 4 narrowed `vi.fn()` vs `AuthFailureDeps` signatures, `@xyflow/react` made more NodeProps Required, + unused-import/redeclare cruft. All type-only/config; runtime 22/22, app typecheck still 0.

## What didn't / lessons
- **Read-path asymmetry survives because the save RESPONSE masks it** — the save handler projects assignment from the in-memory spec, so a freshly-saved graph looks complete; only a fresh GET exposes the gap. Regression tests MUST assert on the GET (read path), not the mutation response. (Reinforces the producer-without-reader / read-side-gap pattern.)
- **Enum wire-shape is a cross-cutting trap** — global `JsonStringEnumConverter` means every API enum is a string; a hand-written numeric FE type only "works" for the write direction (System.Text.Json accepts numeric input) and silently breaks on read. Survey house convention before choosing normalize-vs-retype.
- **`tail -N` truncation lied about scope** — first reported "3 pre-existing typecheck:test errors"; full count was 26 across 4 files. Always count (`grep -c`) before scoping.
- **Background python heredoc opened a REPL** (bad here-doc delim under git-bash) — use PowerShell `ConvertFrom-Json` for parsing structured logs on Windows.
- **DTC exception was Error-level but only discoverable by CorrelationId** — generic toast + no Serilog request-completion log; had to trace by the request's CorrelationId to surface the `Unhandled exception`.

## Decisions made
- **Assignment mode fix → Option B (FE string-native), not normalize-on-read shim** — house convention decided it (zero numeric enum unions elsewhere); a shim would entrench the anomaly.
- **BP-run DTC fix → Approach A (shared request-scoped connection + ambient UoW), cross-platform** — user confirmed Linux/Docker deploy; rejected `ImplicitDistributedTransactions=true` (dies without MSDTC). Plan only, not implemented.
- Did NOT auto-commit the typecheck:test fix — awaiting user OK.

## Test status
- BE: WorkflowDefinitionsApiTests 9/9; build 0W/0E (Workflow.Application).
- FE: typecheck 0, typecheck:test 26→0, editor unit 8/8, affected-test runtime 22/22, lint 0.

## Open: BP-run "Khởi tạo quy trình thất bại" (DIAGNOSED, NOT FIXED)
Root cause (verified exception): `StartBusinessProcessRunCommandHandler` opens ≥2 module DbContext connections (Workflow + Forms, + Identity via Phase-5b first-task resolution) inside ONE ambient `TransactionScope` → distributed-transaction promotion → `NotSupportedException` (no MSDTC on .NET 10 / Linux). Why-now: Phase 5b commit `10ae6203` wired first-task-on-creation → more cross-module fan-out in scope. `ExecuteBusinessProcessTransitionCommandHandler` has the same latent bug. Plan: `plans/260527-2349-bp-cross-module-dtc-fix/` (3 phases, Approach A). NOT related to this session's FE commits.

## Next session
1. Decide commit for typecheck:test fix (4 files) + whether to gate `typecheck:test` in CI (root-cause prevention — it ran silently broken).
2. DTC fix: resolve 3 open Qs in plan (prod `MultipleActiveResultSets`, rollback-test seam, accept new UoW pattern) → `/ck:cook phase-01`.
3. Optional i18n: add `workflow.designer.applied` / `applyValidationFailed` keys to vi/en resource files (currently inline fallbacks).
4. Push 3 committed fixes (origin + bitbucket) — currently local, ahead origin/main by 3.
