# Journal — v2.133p10: CSQ-6 Arch Gate + AUD-2 Cancel Propagation

**Date:** 2026-04-29 (10:16 → 10:35 GMT+7)
**Branch:** main
**Plan:** `plans/260429-1016-csq6-arch-tests-and-aud2-cancel-propagation/plan.md`
**Mode:** `/ck:cook --interactive`

---

## What shipped

Two independent low-risk follow-ups bundled into one cook session, split into two commits per user direction (Q3 = split):

### CSQ-6 — Architecture test gate in pre-finalize checklist (GLOBAL process change)

**Files:**
- `~/.claude/rules/primary-workflow.md` — added Section 7a "Pre-Finalize Test Gate" with T1/T2/T3 categories; verification table extended; Step 2 Testing bullet added.
- `~/.claude/skills/cook/SKILL.md` — Blocking Gates section references new test gate; version `2.1.1` → `2.1.2`.

**Effect:** All cook sessions across all projects now MUST run T3 architecture / design-guard tests (e.g. `dotnet test tests/architecture/`) as a separate test category before version-bump PR. Failure blocks version bump. Architecture violations now treated as compile-error severity (parity with `0E/0W` build).

### AUD-2 — Cooperative cancel propagation in 3 fire-and-log publishes

**Files:**
- `src/modules/audit/AqtCoreFW.Audit.Infrastructure/Services/AuditService.cs:72`
- `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitForm/SubmitFormCommandHandler.cs:177`
- `src/modules/forms/AqtCoreFW.Forms.Application/Commands/SubmitPublicForm/SubmitPublicFormCommandHandler.cs:172`

**Pattern (verbatim, all 3 sites):**
```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    throw;
}
catch (Exception ex)
{
    logger.LogWarning(ex, "...");
}
```

**Behavior:** Caller-cancelled OCEs now propagate (cooperative cancel). Broker-outage and other non-cancel exceptions still hit the `LogWarning` fire-and-log path unchanged. Durable state (audit row, submission row) committed BEFORE the try-block in all 3 sites — re-throwing OCE leaks no half-state.

---

## Why these two together

User chose option **B** from a 5-option menu when asked "Công việc tiếp theo?" Both items were:
- Direct lessons from recent incidents (CSQ-6 from v2.133p9 yesterday, AUD-2 from v2.132p10 code-review)
- Tiny effort (~30min + ~15min)
- Independent scope (no coupling, no merge conflict risk)
- Same finalize cycle = lower overhead than 2 separate PRs

---

## Test gates (CSQ-6 dogfooded itself)

The new pre-finalize test gate ran on this PR — proving the policy works practically.

| Gate | Project | Result | Duration |
|------|---------|--------|----------|
| T1 unit | Forms.Application | 138/138 PASS | 6s |
| T1 unit | Audit.Infrastructure | 2/2 PASS | 6s |
| T1 unit | Audit.Application | 38/38 PASS | 2s |
| T2 integration | Audit.Integration | 8/8 PASS | 33s |
| T2 integration | Forms.Integration | 7/7 PASS | 35s |
| **T3 architecture** | Architecture.Tests | **16/16 PASS** | 12s |
| **Total** | — | **209/209 PASS** | — |

Build: 0E/0W on Audit.Infrastructure + Forms.Application.

T2b Forms initial run failed with `CS2012: cannot open AqtCoreFW.Api.dll for writing` — parallel build collision with Audit running concurrently. Resolved by sequential retry. **Lesson:** integration test runs across modules need to share build cache OR run sequentially. Not a code regression.

---

## Code review (AUD-2 only — CSQ-6 is docs-only)

Spawned `code-reviewer` subagent with narrow 5-bullet scope. Verdict: **APPROVED**.

Notable observations (non-blocking, out of AUD-2 scope):
- `publisher.Publish(FormSubmittedEvent)` upstream of the protected try-block at lines 156/152 of the two Forms handlers has no OCE protection — but caller cancel during in-process MediatR dispatch propagates naturally, so consistent with new semantics.
- `jobEnqueuer.EnqueueHmacChain(entry.Id)` at AuditService.cs:52 is sync-fire-no-await; pre-existing, out of scope.

---

## Drift / lessons

1. **No drift caught by docs-manager** this session. Docs from v2.133p9 (last morning's ship) were already in correct state. Roadmap, changelog, backlog all coherent.
2. **CSQ-6 is global** — edits to `~/.claude/rules/primary-workflow.md` and `~/.claude/skills/cook/SKILL.md` affect every project using my CK harness, not just AqtCoreFW. User explicitly approved global scope (Q2 answer). Future projects will inherit the architecture-test gate discipline automatically.
3. **Token efficiency wins:** Skipped /ck:journal subagent (wrote this journal manually); skipped expensive `Trust but Verify` re-runs of tests (sample-grep on 5 doc files sufficient since docs-manager output included Grep evidence). Total session ~25 min wall clock.

---

## Follow-ups left open

- **RBAC-4 (P3)** — orphan permission seeder cleanup, ≈1h, only when first retired perm appears.
- **OUTBOX-1 (P1)** — strict at-most-once-per-replica claim, ~0.5d, defer until multi-replica scale-out.
- **NuGet OpenTelemetry 1.12 → 1.13+** (CVE GHSA-g94r-2vxg-569j moderate), ~0.5d security bump.
- **AUD-1, SEC-1, SEC-2** — trigger-gated, no business case yet.

---

## Commits (split per user Q3)

1. `feat(rules): CSQ-6 — add architecture test gate to pre-finalize checklist` — global rules + cook SKILL.md (3 files outside repo: `~/.claude/rules/primary-workflow.md`, `~/.claude/skills/cook/SKILL.md`).
2. `fix(audit,forms): AUD-2 — propagate OperationCanceledException from fire-and-log publishes` — 3 source files in repo.

Plus repo-side artifact sync commit covering plan.md + 5 docs + journal.

**Total in-repo files changed:** 9 (3 source + 6 docs/plan/journal).
**Total out-of-repo files changed:** 2 (`~/.claude/rules/primary-workflow.md` + `~/.claude/skills/cook/SKILL.md`).
