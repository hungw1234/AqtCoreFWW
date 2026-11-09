---
date: 2026-05-23
time: 19:24-22:13 ICT (~2h50min)
slug: rt2-15-scaffold-and-q-fold
type: scaffold-session
session_ships: 1 (backlog row only — scaffold/plan-doc work otherwise gitignored)
related_commits: [7a3f4440]
plan_dir: plans/260523-1924-rt2-15-clause-modes-deferred/ (gitignored local)
related_memory: phase-savegraph-assignment-260523 (260523-D fail-CLOSED invariant origin)
---

# RT2-15 Clause-Modes Scaffold + Q-Fold

## Session goal

Scaffold a complete plan for RT2-15 (wire 4 deferred `AssignmentClauseMode` producers — PreviousHandler/ByRole/ByGroup/ByOrgUnit) BEFORE Monday implementation slice. Honor fatigue floor 260522-M (26 ships today, late Sunday evening) → NO production code; only docs + ratifications.

## What worked

- **Scaffold-only discipline held.** Plan dir written entirely under `plans/**/*` (gitignored per memory 260522-G). Single committed artifact = `7a3f4440 docs(backlog): T3-WF-CLAUSE-MODES-RT2-15 P2-product row + 3 PQ gates` — backlog row + PQ gate metadata only. Zero `.cs` / `.tsx` touched all session.
- **PQ + RGQ ratified in-session** (user-as-product mode):
  - **PQ-1 ByRole** → A direct AspNetUserRoles only (no group-inheritance)
  - **PQ-2 ByGroup** → B active group + active user (`UserGroupMembership.IsActive` empirically absent → ceiling is B not C)
  - **PQ-3 ByOrgUnit** → A flat + active temporal assignment (auto-refined: `ApplicationUser.OrgUnitId` doesn't exist → resolves via `UserOrgUnitAssignment` M:N temporal)
  - **RGQ-1 Resolver** → A async-convert (~7-caller cascade, breaking change all-or-nothing)
  - **RGQ-2 ByGroup Save** → B hit Identity at Save (new `IIdentityModuleClient.ResolveActiveUserGroupsByCodesAsync`)
  - **RGQ-3 OrgUnit key** → A Code string (mirror PermCode)
  - **RGQ-4 Clock** → A inject `TimeProvider` (.NET 8 standard)
  - **RGQ-5 FE picker** → C defer to Phase 04 UI scout (~5min slice-start)
- **Empirical scout caught 3 entity-shape drifts BEFORE plan committed** (memory anchor 260522-A re-grep at slice-start INVARIANT honored):
  - `ApplicationUser.OrgUnitId` ❌ does NOT exist (M:N via `UserOrgUnitAssignment`)
  - `UserGroupMembership.IsActive` ❌ does NOT exist (only `AddedAtUtc`+`AddedBy`)
  - `IWorkflowHandlerResolver.Resolve()` ❌ NOT async (contradicted my memory-recall claim — caught by `grep` line 39 = sync `HandlerResolution Resolve(`)
- **Planner re-spawn fold** (~153 lines added across 5 phase docs) — surgical section-level edits per Q, no full-file rewrites. Phase 03 + Phase 05 stayed minimal per scope constraint.

## What didn't / lessons

- **Memory-recall drift caught by empirical probe.** I claimed `IWorkflowHandlerResolver.Resolve()` was async per memory `5b-A`; empirical `grep` showed sync. Fixed by surfacing as Q1 with empirical truth. **Generalizes 260522-A:** memory anchors decay too — re-grep file line at slice-start, do NOT trust prior-session claims about signatures.
- **PQ-3 speculative answer required orchestrator auto-refinement.** User answered "flat node — user where OrgUnitId == X" but the entity shape forbids that interpretation. Auto-resolved as temporal active-assignment with user-override hook for Monday review. Worked tonight but flags a class — when product Q presupposes an entity shape that doesn't hold, orchestrator MUST surface the contradiction BEFORE ratifying (not after).
- **PQ-2 Option C speculative.** Offered "filter `UserGroupMembership.IsActive` if column exists" without grepping first. Empirical scout proved C wasn't a real option (no column). Recovered cleanly because user picked B, but speculative options waste ratification cycles.
- **Phase docs written BEFORE Q ratification = stale at birth.** Created task to re-spawn planner Monday but folded same session via `/cook --auto` because lighter than carry-fwd risk. **Future:** when scaffold + Q-rounds in same session, sequence Q-rounds FIRST, then phase docs (single-pass authoritative).
- **No script for `cook --tdd <backlog-id>` interpreting row + scaffolding plan** — manual scaffolding worked but is repeated process. Backlog-anchored cook is a recurring pattern worth a slash-command sub-flow.

## Decisions made

- **Stack started** (Auth:5002, API:5001, FE:5173, Vault pre-seeded) via `/aqt-runapp` at 19:08 — only to debug user's FE dropdown screenshot showing 3 modes (BY-DESIGN per RT2-15 deferred contract). No code interaction with stack after debug confirmation.
- **HARD-GATE honored 2×** — user opted "B Full row — sai, row BLOCKED" at first /cook attempt; then "C Trả lời cả 5 Q bây giờ" at planner DONE_WITH_CONCERNS. No production code path bypass.
- **Fatigue floor 260522-M overrides ratified** for scaffold scope only (mechanical doc edits, no decisions, reversible).
- **Phase 04 Q5 deferred BY DESIGN** to slice-start UI scout (~5min) — Q5 is a UI-discoverable question, not a product-policy question; defer is cheaper than premature commit.

## Test status

N/A — no production code, no test runs this session. Validation deferred to Monday Phase 01 slice via `--tdd` flag in plan dir.

## Effort estimate revised

| Phase | Original | Post-Q-fold |
|-------|----------|-------------|
| 01 PreviousHandler | 1-2h | **3-5h** (+Q1 async-cascade 7 callers) |
| 02 ByGroup | 3-4h | **5-7h** (+Q2 Identity Save-validation hop) |
| 03 ByRole | 3-4h | 3-4h (inherits Q1 only) |
| 04 ByOrgUnit | 4-6h | **5-7h** (+Q3 Code-key, +Q4 TimeProvider DI, +Q5 scout) |
| 05 Validator sweep | 1-2h | 1-2h (+Q2 cross-ref note) |
| **Total** | ~12-18h | **~18-25h (~3-4d)** |

Q1+Q2 expansion = +5-8h. Still slimmer than initial 4.5-5.5d est because PQ-ratification removes re-decision cycles. Memory anchor 260522-O (planner over-allocates 5-9× for pattern-mirror) applies to Phase 03/05; Q1 cascade is NEW work, doesn't get the discount.

## Next session

1. **Audit-at-slice-start (260522-A INVARIANT):** Re-grep `_notSupportedModes` + FE editor option list + 3 entity-shape findings (decay risk in scaffold gitignored locally — scaffold may be stale by Monday if upstream changes)
2. **Verify `Microsoft.Extensions.TimeProvider.Testing` NuGet** in Forms/Workflow test csproj (Q4 FakeTimeProvider dep) — trivial add if missing
3. **Open Phase 01:** `/cook plans/260523-1924-rt2-15-clause-modes-deferred/phase-01-previous-handler.md --tdd` (lowest dep, no PQ; ~3-5h estimated post-Q1)
4. **Watch for:** Q1 async-cascade breaking change affecting `CreateWorkflowInstanceCommandHandler` / `InProcessWorkflowModuleClient.CreateInstanceAsync` / `ExecuteTransitionCommandHandler` + NSubstitute mocks — Phase 01 owns the cascade per plan
5. **Phase 02 product-input check:** confirm Identity module currently has a client interface OR Phase 02 creates new `IIdentityModuleClient` interface in shared abstraction
6. **At Phase 04 slice-start:** ~5min UI scout for OrgUnit picker (`staff-assignment-table.tsx` reuse vs new multiselect)

## Lessons → memory anchor candidates

| Candidate | Pattern | Promotion criterion |
|-----------|---------|---------------------|
| **260523-E** memory-recall about file signatures decays — re-grep line at slice-start | Empirical INVARIANT extends 260522-A from backlog rows to memory file-claims | Promote on 2nd reinforcement |
| **260523-F** product-Q answer presupposing entity-shape — orchestrator must contradict-before-ratify | Auto-resolve OK tonight but classes as risk | Single instance — observational |
| **260523-G** scaffold + Q-ratification same session → sequence Q FIRST, phase-docs SECOND | Single-pass authoritative beats fold-after | Folded same session via /cook --auto — workflow lesson |

## Unresolved questions

- Whether Phase 04 `Microsoft.Extensions.TimeProvider.Testing` is already in test csproj (Monday grep, trivial)
- Whether new `IIdentityModuleClient.ResolveActiveUserGroupsByCodesAsync` creates new interface OR extends existing (Phase 02 slice-start scout)
- Whether Q1 async-cascade affects integration-test fixtures via DI registration shape (Phase 01 slice-start grep `IWorkflowHandlerResolver` usages — memory says ~7 callers, verify count)
