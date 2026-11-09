# Journal — MTS-1 Regression Pass (2026-05-12 08:30 ICT)

## What

Ran MTS-1: full regression pass over the 2026-05-11 manual-test-session — 6 commits `e646fc3f..7c956a9a` on `main` (14 bugs/enhancements: zero-config dev boot + global Dapper `DateTimeOffset` handler, T2 `PublicId AS Id` materialization across Identity/Files/Notifications/Workflow, Workflow Visual Designer persist + EF migration `AddVisualDesignerPositionsAndHandles`, orphan `admin-overview` menu leaf removal, VGCA/Mysign vendor SDK reference).

## Result — NO REGRESSION

| Surface | Result |
|---|---|
| `dotnet build` | 0 errors, 2 NU1510 warnings |
| Unit (Workflow/Files/Notifications/Identity — changed modules) | 867/867 |
| Integration — Identity / Files / Notifications | 198 + 7 + 7 = 212/212 |
| Integration — Workflow | 14/24 (8 fail, 2 skip) — **pre-existing** |
| FE typecheck (`tsc --noEmit`) | clean |
| FE vitest | 861/871 (10 pre-existing fail) |
| Architecture suite | **does not compile** — pre-existing, pre-dates MTS |

The 8 Workflow.Integration failures were not asserted pre-existing — they were **proven** so: `git worktree add` at parent commit `c841ebf6`, ran the suite there, got byte-identical 14/24 (8 fail, 2 skip). The new EF migration is provably innocuous: 4 additive nullable columns (`SourceHandle`/`TargetHandle` `nvarchar(max)`, `PositionX`/`PositionY` `float`), no FK or Id-type changes.

Report: `plans/reports/tester-260512-0830-mts1-regression.md` (incl. bisect verification section).

## Decisions / Findings

1. **`AqtCoreFW.Architecture.Tests` has not compiled since W7 signing commit `bec64e2d` (2026-05-11).** Two errors: `SignatureProviderCapabilityArchTests.cs:110` CS1929 (`MethodInfoAssertions` has no `NotHaveAbstractBody` — FluentAssertions API removed); `CallbackAuthArchTests.cs:76` CS0234 (`HttpMethodAttribute` is in `Microsoft.AspNetCore.Mvc.Routing`, not `.Mvc`). Consequence: the "Architecture 87/87 PASS" recorded in the signing-core and QD742-Phase-05b ship summaries was **inaccurate** — the arch guard suite (T1/T2 tier shapes, Dapper schema escape, DI host guards) has not run since W7. Spawned backlog **MTS-9 (P2)**. Lesson reinforced: subagent-wave ship reports' test counts can be unverified — re-run the suite before trusting "N/N PASS" in a ship summary.
2. **Workflow.Integration at 58% pass is pre-existing and was never triaged.** 3 buckets — (a) ×3 `UniqueActiveInstanceIndexPreflightCleanupTests` "uniqueidentifier incompatible with bigint" (test seeds Guid where `WorkflowInstance.Id` bigint post Phase-04b); (b) ×3 FK MERGE conflict on `FK_WorkflowStates_WorkflowDefinitions_DefinitionId` (`ExecuteTransitionAsync_SubmitFromDraft`, `SequentialRace_SecondTransition`, +1 — likely the already-tracked `PHASE7-WF-INLINE-FACTORY-FK-DRIFT`); (c) ×2 `ListDefinitions_*` (403 / "no JSON tokens" from seeding setup). Spawned backlog **MTS-10 (P2)**.
3. Repo TFM is **net10.0** (SDK 10.0.201), not .NET 8/9 — corrected a stale assumption.

## Follow-ups

- MTS-9 (P2): fix Architecture.Tests compile (≈30-45m) → re-run full suite → fix any genuine guard regressions → restore green baseline. Priority-bump candidate.
- MTS-10 (P2): triage + fix the 3 Workflow.Integration failure buckets (≈2-3h).
- MTS-1 itself: DONE — no further action.
