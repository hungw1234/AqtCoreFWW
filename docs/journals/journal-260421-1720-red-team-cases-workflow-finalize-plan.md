# Journal — Red Team Rewrite of Cases Workflow Finalize Plan

**Date:** 2026-04-21 17:20–18:20 (Asia/Saigon)
**Branch:** main (post-pull @ `878d198f` v2.127)
**Baseline state at start:** local `33ceb090` v2.126; remote `878d198f` v2.127 (PR #48 SoD Warn+Audit Refactor merged but un-pulled)
**Activity type:** Planning only — no implementation code written.

## Session Intent

Follow-up after v2.126 Cases→Workflow button unification: identify remaining gaps between Cases and Workflow modules, propose + plan cleanup. Local dev stack was also started for manual verification.

## Timeline

1. **Local dev start** via `/aqt-runapp` skill — Auth:5002, API:5001, FE:5173 all listening (cold start API ~90s). Chrome MCP unavailable; URLs surfaced to user.
2. **Inbox explainer** — clarified `/inbox` route = Case-level personal queue (not messaging); identified `features/workflow/workflow-inbox-page.tsx` as dead code (legacy WorkflowTask-level inbox superseded).
3. **Deep scout via `/ask`** — BE + FE parallel scouts of Cases↔Workflow contract. Result: 4 real gaps (inbox dead code, parallel-branch UI scope, test coverage debt, hardcoded action names). Flagged 2 medium gaps (at-most-once integration events, state-name drift risk).
4. **User answered 4 unresolved Qs** — Q1 no parallel branch prod usage; Q2 Case-only state validation scope; Q3 "switch RabbitMQ now"; Q4 dead-code sweep OK.
5. **`/ck:cook` Option 1** → `planner` subagent created plan v1 at `plans/260421-1752-cases-workflow-finalize/` (3 phases, 2-3d effort, order 02→01→03).
6. **`/ck:plan red-team`** → 3 hostile reviewers (Security + Failure + Assumption) in parallel. **Verdict: BLOCKING.** 15 findings (6 Critical + 6 High + 3 Medium). Three reviewers converged on the same 4 Critical items.
7. **User chose Option A (full rewrite)** → planner subagent overwrote 4 plan files with v2 addressing every finding. Effort revised **2-3d → 5-8d**. Execution order flipped to **03 → 02 → 01**.

## Key Red Team Findings (why plan v1 was unshippable)

- **C1 — OutboxProcessor is a stub.** Only `Task.Delay` loop, zero SELECT / publish / mark logic. Plan v1 claimed "wire existing infra"; actual work is implementing the dispatcher (3-5d, not 1-2d).
- **C2 — Outbox infra already DI-registered** (`InfrastructureRegistration.cs:67-74`). Plan v1 scout used wrong grep metric.
- **C3 — `OutboxMessage` has no TenantId column.** Multi-tenant isolation break (P0 per `multi-tenant-deployment.md`).
- **C4 — `CreateWorkflowDefinitionCommand` has no `EntityType` field; `Update` has no `States` field.** Phase 02 validator as spec'd would not compile.
- **C5 — `WorkflowTaskDto` has 3 live callers** (`useTasksByInstance`, `useDelegateTask`, `useReturnDelegation`). Cannot be deleted.
- **C6 — `router.ts:32` imports `workflow-inbox-page` from `routes/`** (active route) — plan v1's "zero router import" claim was misleading due to filename collision with the dead `features/workflow/workflow-inbox-page.tsx`.
- **H-tier** included: MassTransit `UseInMemoryOutbox` conflicts with custom Outbox; `CaseTransitionedIntegrationEvent` doesn't implement `IExternalDomainEvent`; `WorkflowDefinitionsController` has bare `[Authorize]` (RBAC gap); validator missed Transitions.From/To; rollback leaves interceptor writing rows.

## Plan v2 Deliverables

4 overwritten files at `plans/260421-1752-cases-workflow-finalize/` (gitignored path, not tracked):
- `plan.md` — 83 lines, effort 5-8d, exec order 03→02→01, Red Team Review section (15 findings, all accepted).
- `phase-01-outbox-rabbitmq-activation.md` — 171 lines. Scope expanded: MassTransit EF Outbox swap, TenantId migration (Designer+Snapshot), `CaseTransitionedIntegrationEvent : IExternalDomainEvent` refactor, `CaseTransitionHelpers` → `AddDomainEvent` pattern, `VHost`/`VirtualHost` fix, CI broker-kill commitment, git-revert rollback.
- `phase-02-workflow-state-validation.md` — 144 lines. API change first (add `EntityType` to entity + command + DTO + FE + migration), validate states AND transitions, RBAC policy hardening, seeder+DB casing scan as first gate.
- `phase-03-dead-code-sweep-workflow-inbox.md` — 108 lines. Scope narrowed: KEEP `WorkflowTaskDto` + `routes/workflow-inbox-page.tsx` + `task-detail-drawer.tsx`; delete only `features/workflow/workflow-inbox-page.tsx` + exclusive i18n keys.

## Learnings (memory-worthy)

- **`feedback-red-team-before-implement.md` validated again.** ~6 min of red team caught 5-8d of avoided rework. 3 reviewers converging on identical Critical items = strong signal (not false positive).
- **Scout metric matters.** Plan v1 scout used `grep OutboxProcessor usage` = 0 and concluded "not wired." Correct query is `grep AddHostedService<OutboxProcessor>`. Grep for symbol usage ≠ grep for DI registration.
- **Planner subagents trust caller-provided scout findings** — must verify critical claims independently before committing to plan structure. Orchestration-protocol "trust-but-verify" applies to scouts handed to planners, not just to verifier-agent reports.
- **Filename collision is a real hazard.** `features/workflow/workflow-inbox-page.tsx` (dead) and `routes/workflow-inbox-page.tsx` (active route) would both match a naive `grep -r workflow-inbox-page`. Always distinguish directory when claiming "not imported."

## Non-plan Changes This Session

- Pulled `origin/main` fast-forward: `33ceb090 → 878d198f` (PR #48 v2.127 SoD landed).
- Fixed docs gap missed by PR #48 docs-manager: `project-roadmap.md` top header still said v2.126 (rest of file already had v2.127 content) — bumped header and Phase Overview title.

## Follow-ups

- Plan v2 needs `/ck:plan validate` pass before `/ck:cook` execution (5 planner-flagged unresolved questions require ops/business input — OutboxMessage table fate, DbContext ownership for MT EF Outbox, TenantId backfill strategy, `Workflow.Def.View` policy naming, `EntityType='Case'` default on backfill).
- Suggested execution: Phase 03 first (0.5d low-risk win after red-team scope narrowed) → validate gate → Phase 02 (1.5-2.5d API change) → Phase 01 (3-5d dispatcher, isolate rollback window last).
- Red Team Review section in `plan.md` is a permanent artifact — future sessions can reference findings instead of re-running red team.

## Baseline Commit

Session baseline: `878d198f` (main, v2.127, SoD Warn+Audit Refactor).
This session's commit: `21236781` (docs + journal sync only; no source code changed).
