# Journal — BP-TRANSITION-FORM-E2E (v2.137p150)

**Date:** 2026-06-03 · **Commit:** `09c72b88` (test) · **Pushed:** origin + bitbucket, ahead=0 · **Tier:** T4-Low

## What shipped

Live e2e coverage for the BP transition `requiresForms` gating path — two NEW test-only files, zero production change:

- **BE integration** `ExecuteBusinessProcessTransitionHttpTests.cs` — 2 facts driving the real `POST /api/v1/business-processes/runs/{id}/transitions` over WebApplicationFactory + Testcontainers SQL 2022.
- **FE Playwright** `business-process-transition-form.spec.ts` — live-backend + graceful-skip journey that opens the confirm modal with the inline form.

BE 2/2 PASS (46s) · build 0E/0W · FE eslint + playwright-compile clean · code-review APPROVE 0 Crit/High/Med.

## Key decisions

1. **Audit-at-slice-start caught a stale premise.** The row's "no seed configures one" was stale — `DemoBusinessProcessSeeder.cs:203-205` now binds a *required* form to the Submit transition (`DemoBusinessProcessSeederTests` asserts it). But that only proves the binding *persists*; the live HTTP short-circuit + the two runtime Dapper queries (`GetTransitionFormTemplateIdsAsync` + `GetVersionSchemaByInternalIdAsync`, both mocked by the unit test) were still uncovered. Verdict: category (b) partial-stale — proceed, correct the premise in the close-note. The deliverable was real, the justification changed.

2. **"The run is the gate" surfaced a fail-closed design, not a test bug.** The original Fact 2 ("submit + advance state") returned 422 `GOV_WFL_004: Action 'submit' requires role 'Handler'`. Reading `ExecuteTransitionCommandHandler.cs:154-161` showed the cause: a transition with **no** `WorkflowTransitionAssignment` is denied (fail-CLOSED by design); "Handler" is just the error label, and `IsActorAuthorized` resolves grants from the DB via `identityClient`/`orgUnitClient`, not JWT claims — so adding a role to the auth header would not help.

3. **Descoped submit+advance from BE rather than balloon scope.** Standing up the workflow-authz apparatus (identity stub + assignment seeding, which lives in the Workflow test project) for a T4-Low confirmation item is disproportionate. The submit-then-advance journey is already covered by: the FE Playwright spec + the handler unit happy-path-with-form (`Received.InOrder` form-before-execute) + the Workflow module's `ExecuteTransitionContractTests` (which seeds the authz and asserts the run advances). Fact 2 was rescoped to the binding-guard (form supplied + unbound template → 422, pre-authz) — a stable, in-scope assertion that proves the form-bundled branch is entered without touching the authz apparatus. Documented in the test doc-comment.

## Lessons

- **A "small test" task's risk is the ARRANGE, not the assertion.** Three load-bearing arrange facts had to be verified before writing: tenant alignment (no fixed test tenant — seed with the same tenant the client authenticates as), publishing a real `FormTemplateVersion` (so the snapshot fetch is un-mocked), and creating a `WorkflowInstance` parked in the initial state. Two Explore agents mapped these before any code.
- **Verify the failure cause before choosing the branch.** The 422 could have been form-submission or transition-exec failure; reading the actual error body (`GOV_WFL_004`) + the handler line pinned it to fail-closed authz, which made descope the proportionate call rather than guessing at a fix.
- **Measure, don't infer, where cheap.** Code-review flagged that Fact 1's "no state change" was inferred via `success==false`; hardened to re-query `CurrentStateId` and assert it equals the initial state.

## Residual / carry-forward

- FE spec is **dormant against the out-of-box seed**: the demo BP ships Draft (`IsActive=false`), and the spec's first precondition queries `/active`, so it skips until an operator activates the BP. Noted in-spec so a future reader does not mistake green-but-skipped for coverage.
- Open backlog: `BP-RUN-DOSSIER-LISTS-PHASE-5-PERF` (query-plan + load, blocked on bulk-seed helper).
