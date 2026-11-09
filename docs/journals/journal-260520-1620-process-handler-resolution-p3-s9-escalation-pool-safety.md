# Journal — PHR-V2 P3 Slice 9: EscalationCheckJob Pool-Safety + Pool-Aging

**Date:** 2026-05-20 16:20
**Branch:** `feat/process-handler-resolution-p3` @ `b8cdee46` (LOCAL, 26 ahead `origin/main`)
**Version:** v2.137p81
**Slice scope:** RT2-7 steal-fix + RT3-M3 log-only pool-aging; no schema change; 5 new integration tests
**Phase 3 progress:** 8/11 → 9/11 = 81.8%

---

## Outcome

Slice 9 SHIPPED LOCAL. `EscalationCheckJob` no longer steals pool tasks (RT2-7 resolved). A second pass
emits structured `LogWarning` for unclaimed pool tasks aged ≥ 60 min (RT3-M3 mandatory — no build may
ship pool tasks without aging). 5 integration facts (A1-A5) all GREEN.

**Commit: `b8cdee46` — 2 files, +758/-46 LOC**

---

## What shipped

| Category | File | Change |
|----------|------|--------|
| **Prod (MODIFIED)** | `EscalationCheckJob.cs` | +97/-46: SQL filter `AND t.AssigneeId IS NOT NULL`, belt-and-braces guard, `PoolAgingThresholdMinutes = 60` const, 2nd-pass aging SQL + LogWarning loop; early-exit restructured to nested-if |
| **Test (NEW)** | `Jobs/EscalationCheckJobPoolSafetyTests.cs` | +663L: A1-A5 facts, `CapturingLogger<T>`, raw SQL backdating, exact-boundary log filters, S5 META-L (c) precondition asserts |

---

## Deviation from planner spec — rationale

Planner spec called for `if (overdueTasks.Count == 0) return;` early-exit intact, with aging block after
`await ctx.SaveChangesAsync(ct)`. This structure would have blocked the aging pass entirely when no
overdue single-assigned tasks exist (A3's scenario: pool task with no DueDate, so escalation scan returns
empty → early return → aging never fires → A3 would remain GREEN-trivially, never detecting the absence).

**Applied fix:** Restructured to `if (overdueTasks.Count > 0) { ...escalation+save... }` nested block so
aging pass always runs regardless of escalation results. A3's RED at step 3 (before aging was added)
PROVED this mattered — overdueTasks.Count WAS 0 in A3's scenario.

**Lesson:** Plan spec was written before A3's specific scenario (pool task, no DueDate) was pinned.
Planner anticipated the restructure was needed but wrote the spec as a rough guide. TDD forced the
correct structure empirically.

---

## Gate matrix (S9 locked)

| Gate | Result | Evidence |
|------|--------|---------|
| Build | **0E/0W** | `dotnet build` both integration test + host projects |
| EF drift | **"No changes"** | `dotnet ef migrations has-pending-model-changes` — aging = log-only |
| G1 (coarse policy) | **5/5** | Unchanged invariant |
| G1' (fine gate) | **5/5** | WorkflowFineResolverGateTests |
| G2 (domain) | **101/101** | AqtCoreFW.Workflow.Domain.Tests |
| G3 (integration) | **33p/1f/2skip** | 1f = `SimultaneousRace` pre-existing (git stash proof) |
| G3+ (contracts) | **34p/1f** | 1f = `GetAvailableTransitions_ForRunningInstance` pre-existing |
| G4 (PageCompression) | **1/1** | No new tables; schema unchanged |
| **G5 (A1-A5)** | **5/5 GREEN** | EscalationCheckJobPoolSafetyTests |

---

## Inherited META-LESSONS this slice

**(a) S5 META-L (c): tests-to-impl-lock — CRITICAL win this slice.**

A1 was designed non-tautologically: `EscalateToValue` is a parsable Guid so `task.Assign()` WOULD have
fired without the fix. Without this precondition pinning, the test would pass trivially (Guid.TryParse
fails → escalateToId = Guid.Empty → task.Assign never called → AssigneeId stays null whether SQL
filter exists or not). Precondition asserts proved this: `preParseResult.Should().NotBe(Guid.Empty)`.

**(b) Shared-DB contamination in integration tests.**

Short integer IDs (1, 2, 3...) appear as substring matches inside UUID values embedded in structured log
messages (e.g., `tenant 3407e929-...` contains digit `1`). All log filters required exact-boundary
pattern `$"Pool task {id} "` with trailing space. Without this, A4 and A5 produced false positives from
other tests' pool tasks persisted in the same Testcontainers DB.

**Carry-fwd invariant:** When filtering structured log messages by entity ID in integration tests that
share a DB, always use `$"MessagePrefix {id} "` (trailing space or next field boundary), NEVER bare
`id.ToString()`. Short IDs are unreliable as standalone substring matchers in structured log text.

**(c) TDD forced prod-code deviation from planner spec (A3 scenario).**

Covered under "Deviation from planner spec" above. The early-exit restructure was not a judgment call —
it was empirically required by A3 remaining RED (0 aging logs with early-exit, because overdueTasks was
empty for A3's scenario). TDD cycle protected against a false-GREEN planner spec.

---

## 🆕 NEW META-LESSON: Hand-rolled CapturingLogger<T> for ILogger<T> tests

**Discovery:** NSubstitute's generic `Arg.Any<TState>()` / `Arg.Invoke<TState, Exception, string>(...)` 
matchers on `ILogger<T>.Log<TState>()` fail at runtime with ambiguous method resolution. Every attempt
to configure a NSubstitute `ILogger<T>` mock for log-capture eventually hits this (confirmed in prior
sessions + S9 specifically).

**Fix:** Hand-roll `CapturingLogger<T> : ILogger<T>` (~15 LOC). Stores `(LogLevel, string)` tuples.
Zero NuGet dependencies. Log message materialised by calling `formatter(state, exception)` exactly as
the structured logger would.

**Pattern (pinned for S10+):**
```csharp
private sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
        => _entries.Add((logLevel, formatter(state, exception)));
}
```

Use this pattern any time a test needs to assert on log output from a class under test. Do NOT reach for
NSubstitute's mock for `ILogger<T>`.

---

## Decisions ratified (D1-D3, Q1-Q5)

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| D1 | Aging delivery | Log-only | KISS; no DB mutation, no event; observable via log scrape |
| D2 | Aging integration | Integrated 2nd pass | Same job, disjoint SQL; no new Hangfire registration |
| D3 | Aging threshold | 60-min const `PoolAgingThresholdMinutes` | Tuned to typical task SLA; NOT per-tenant config (P5 future) |
| Q1 | Belt-and-braces guard | YES | SQL filter already excludes pool tasks; guard defends against future SQL drift |
| Q2 | Separate 2nd SQL query | YES | Disjoint predicates: Pass 1 = AssigneeId IS NOT NULL; Pass 2 = AssigneeId IS NULL |
| Q3 | Logger mock strategy | Hand-rolled `CapturingLogger<T>` | NSubstitute generic method resolution fragile |
| Q4 | A5 co-existence test | INCLUDED | Proves passes are disjoint at runtime |
| Q5 | UpdatedAt assert in A3 | ADDED | `taskAfter.UpdatedAt.Should().BeNull(...)` — aging must not trigger audit-update side-effect |

---

## Cost analysis

- **Planner ratification + plan reading:** ~10 min
- **A1 TDD (RED + fix + GREEN):** ~25 min (Guid trick for non-tautological test + SQL filter)
- **A2-A5 TDD (all passes):** ~30 min (shared-DB contamination debugging ~15 min)
- **Aging pass restructure (deviation from spec):** ~10 min
- **Advisor pre-finalize + nit cleanup:** ~10 min
- **Gates (build, EF, G1-G5):** ~8 min
- **Plan sync + journal + docs:** ~10 min

**Total: ~103 min.** Most time = A1 non-tautological proof (~25 min) + shared-DB log contamination fix (~15 min) + aging restructure + advisor cycle.

---

## Carry-forward to Slice 10 (Override 3-layer thread)

- **S9 invariants carry:**
  1. Exact-boundary log ID pattern `$"MessagePrefix {id} "` in shared-DB integration tests
  2. CapturingLogger hand-roll pattern for `ILogger<T>` mock
  3. TDD non-tautological: pin preconditions that PROVE code path fires (S5 META-L (c))
- **S10 scope:** 3-layer override (ETR + BP + orval regen; dual `ExecuteTransitionRequest` + live `GetUserActiveStatusAsync`)
- **Carry-fwd S8b invariants (still active):** EF drift pre-flight, FQN-based test failure tracking, wire-format enumeration gate

---

## Unresolved

None. EscalationCheckJob pool-safety is complete. RT2-7 + RT3-M3 both closed.

---

## Phase 3 summary (post-S9)

**Slices completed:** S1, S2, S3-S4, S5, S6, S6.1, S7, S7.1, S8a, S8b, S9 = 11 distinct delivery units
**Progress:** 9/11 = 81.8% (S10, S11 remain)
**Schema state:** Column-drop finalized (S8b); EscalationCheckJob pool-safe; no new schema in S9
**Code state:** Assignment-based authz sole path (G1 5/5); pool tasks never stolen (RT2-7); aging log-only (RT3-M3)

**Next slices:**
- **S10:** 3-layer override completion + GetUserActiveStatusAsync live + orval regen (S8b L1 cleanup)
- **S11:** Atomic-merge gate verify (fine-resolver tests + migration script audit + GH UI review)
