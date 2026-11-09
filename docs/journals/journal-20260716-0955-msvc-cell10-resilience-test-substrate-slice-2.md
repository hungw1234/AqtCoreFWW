# Journal — MSVC Cell #10 Resilience Test-Substrate Slice-2 (timeout/transient-retry)

**Date:** 2026-07-16 · **Type:** test-only, microservices completeness-matrix cell #10 slice-2 · **Row:** `MSVC-CELL10-TEST-SUBSTRATE-SLICE2` · **Status:** implementation-complete, uncommitted

## What shipped

Second fault dimension for cell #10 (Resilience), continuing slice-1 (`13ff11e6`, duplication). One new file:
`tests/integration/AqtCoreFW.Forms.Integration.Tests/EventHandlers/TransientFaultRetryConvergenceTests.cs` — 2 tests against a real `AddMassTransitTestHarness` (in-memory transport, `UseMessageRetry(Immediate(N)) + ConfigureEndpoints`, mirroring `MessagingRegistration.cs:64/101`).

- **TC-FRM-INT-RETRY-001** — transient failure (throws on attempt 1) then succeeds on retry → `Received(2)`, no `Fault<T>` published.
- **TC-FRM-INT-RETRY-002** — persistent failure (throws every attempt) → retries exhaust → visible `Fault<WorkflowInstanceFinalizedIntegrationEvent>` published, `Received(3)` (1 initial + 2 `Immediate` retries).

+1 versionless `MassTransit` PackageReference on the test csproj (resolves 8.3.6, zero version movement across the solution).

## The decision worth recording: scout overrode aqt-next's guess

aqt-next's first pass floated "message-reordering" as slice-2. Scout rejected it: no order-sensitive consumer exists anywhere in the monolith today, so a reordering test would be vacuous — asserting a property nothing depends on. Scout instead found a purpose-built target already sitting there: `WorkflowFinalizedFormSubmissionConsumer` is deliberately **effect-idempotent and throws-to-retry** — no dedup ledger, infra exceptions propagate on purpose so `UseMessageRetry` re-applies, and exhaustion surfaces as a visible `Fault<T>` instead of a silently-stranded Pending submission (see the consumer's own header comment, lines 26-28).

That makes timeout/transient-retry the semantic complement of slice-1's claim-first duplication test: slice-1 proves the at-most-once/dedup arm, slice-2 proves the retry-durability/effect-idempotent arm of the same delivery-semantics↔dedup-strategy matrix (`feedback-claim-first-dedup-only-for-at-most-once-consumers`). Picking the target by matching the code's actual delivery-semantics contract, not by working down a generic fault-taxonomy checklist, is the reusable move here.

## The gap it closes

The sibling unit test `WorkflowFinalizedFormSubmissionConsumerTests` proves the consumer throws — via `Assert.ThrowsAsync` against a hand-mocked `ConsumeContext<T>` — and stops there. It never drives real redelivery or dead-lettering, so the actual durability guarantee ("a transient failure converges; a persistent one surfaces a visible fault, never a silent drop") was asserted only at the unit boundary, never proven end-to-end through the retry pipeline. That's the textbook `feedback-test-new-wiring-through-real-registration` false-green: new cross-cutting wiring (`UseMessageRetry`) with zero test driving the real registration path. This slice closes it.

## Verify

Build 0W/0E. 2/2 GREEN. Mutation-proven non-vacuous both ways: Test A assertion `Received(2)` weakened to `Received(x => x >= 1)` → RED (would pass a swallowed-first-attempt bug); Test B `ThrowsAsync` swapped to `Returns(Task.CompletedTask)` → RED (fault never publishes). Reverted both → GREEN.

Full Testcontainer SQL suite **not** run — the added package is additive and untouched by the existing SQL-backed tests; whole-project build-green stands as the regression evidence. Residual, not blocking.

## Review

Code-review: 0 Critical/High/Medium. 2 Low nits applied: clarifying comment on Test B's `Immediate(2)` vs prod's 3-retry count (so a reader doesn't mistake the test's retry count for prod's), and `WaitUntilAsync` now throws with a descriptive message on timeout instead of looping silently past the deadline. One Low accepted as-is: the harness omits outbox/tenant-filter wiring — valid, because `IFormSubmissionStatusClient` is a substitute, so there's no DB/RLS surface to scope in the first place (matches slice-1's minimal-wiring precedent).

## Follow-up filed, not built

`MSVC-CELL10-RETRY-CONFIG-GUARD` (T4, arch-guard) — assert `MessagingRegistration.cs` keeps `UseMessageRetry` on both transports. A regression silently removing it converts every throws-to-retry consumer (this one included) to immediate dead-letter with nothing to catch it. User scoped this round to the 2 harness tests only; this is next, cheap, unblocked.

Remaining cell #10 dimensions: partition / reordering / chaos + cross-service dedup — later, some extraction-dependent (reordering specifically stays vacuous until an order-sensitive consumer exists).

## Lessons reinforced

1. `feedback-test-new-wiring-through-real-registration` — unit-level `ThrowsAsync` on a mocked context is not proof the real retry machinery re-applies; this is the exact false-green closed here.
2. `feedback-claim-first-dedup-only-for-at-most-once-consumers` — picking the fault dimension by matching the target consumer's actual delivery-semantics contract (not a generic checklist) is what made this slice non-vacuous.
3. Scout-overrides-guess: aqt-next's initial dimension pick (reordering) would have shipped a vacuous test; always verify against source before committing scope, echoing `feedback-reverify-plan-before-execute`.

## Unresolved

- None blocking. `MSVC-CELL10-RETRY-CONFIG-GUARD` is filed and ready, not yet scheduled.
