# Journal — WF-TRANSITION-STATUS-WRITE-NO-OUTBOX: Workflow→Forms status write via Outbox

**Date:** 2026-07-08 · **Backlog row:** WF-TRANSITION-STATUS-WRITE-NO-OUTBOX (R1 from the Phase-2 write-path inventory) · **Branch:** main · **Status:** implemented + verified, commit pending

## What & why

The Workflow→Forms approve/reject-on-completion ran through an in-process MediatR handler that called a cross-module client on a separate DbContext. If that write threw an infra error after the workflow transition had already committed, the FormSubmission silently stayed `Pending` while the workflow showed completed — a silent cross-module divergence with no retry/durability. This slice routes the decision through the Outbox (durable at-least-once) so a broker outage or process crash between the transition commit and the status update no longer strands the submission.

## Audit-first (INVARIANT 260522-A) — the row over-stated, but the gap was real

Before opening scope I audited the row against source. Three corrections: the row claimed "zero idempotency/try-catch" (false — the client already has a domain idempotency guard + try-catch for the already-applied race), "Blocks extraction" (contradicted the cited inventory: "neither blocks extraction"), and a wrong code location. The inventory's verified disposition was **file-and-defer, low-materiality**. I surfaced this to the user with three options (full outbox / lighter reliability envelope / re-affirm defer); the user chose the full outbox final-form. The audit still paid: it reframed the problem accurately (a post-commit best-effort cross-module write, not a direct-UoW gap) before any code.

## Design — leverage the existing interceptor, avoid envelope surgery

The feared cost was threading an outbox enqueue through `ExecuteTransitionCommandHandler`'s 615-line transaction envelope (ambient-enlist, BP cooperative-rollback, defer-save). Scouting found the clean path: `WorkflowInstance` is an `IAggregateRoot`, and the existing `OutboxInterceptor` already captures `IExternalDomainEvent`s off aggregates into `<schema>.OutboxMessages` in the same SaveChanges (proven in prod by BusinessProcess). So the aggregate raises a new `WorkflowInstanceFinalizedIntegrationEvent` on finalization; the interceptor writes the outbox row atomically in the transition's own SaveChanges — **zero changes to the delicate transaction structure**. `IOutboxWriter` was the wrong tool here (it requires a `CrossModuleUnitOfWork`, which the transition path doesn't use).

Two facts collapsed scope further: `workflow.OutboxMessages` already existed (created by the module's *initial* migration via the `ModuleDbContext` base — the generated migration was an empty no-op, removed), and the `workflow` schema was already in the outbox dispatch registry. So no migration, no schema registration — just the event, the emission, the consumer, and the tests.

## Two corrections during implementation

1. **Parity gap (caught by verifying the subagent's report).** The subagent emitted the event only in `ExecuteTransitionCommandHandler`. But the removed handler was a `WorkflowTransitionedEvent` notification handler — path-agnostic, firing on *every* `ApplyTransition`. There are two production finalizing call sites: the interactive handler and `SlaBreachCheckerJob` (auto-timeout). Emitting only at the first would silently drop auto-timeout finalizations. Fixed by adding the emission at the SLA site too (chose explicit per-site over a signature change to `ApplyTransition` — 2 call sites vs 9 test call-sites of churn). The parallel-join path never sets `CompletedAt`, so it never finalized under the old handler either → correctly left uncovered (filed as a pre-existing FU).

2. **CRITICAL: claim-first dedup defeated the retry it depended on (caught by code-review).** The consumer copied the claim-first `IProcessedMessageStore` pattern from the notification consumers. But that ledger commits in its own DbContext *before* the Forms write runs in a separate context. On a post-claim infra failure, the exception propagates → MassTransit retries → the claim now exists → the consumer logs "already processed" and returns success → the approve/reject is never applied. It re-created the exact R1 strand in a narrower window, masked as success. Fix (the reviewer's recommendation): **remove the ledger entirely** — the effect is already idempotent (Approve/Reject no-op an already-applied submission and publish nothing on the duplicate), so retries run safely against the effect and exhausted retries dead-letter (visible). Added the missing test: infra failure must propagate, not swallow.

## Verify

Full build 0W/0E · Contract 50/50 · Workflow.Domain 145/145 · Workflow.Application 640/640 · Forms.Application 329/329 (9 new consumer tests incl. the retry-durability propagation case) · Arch 158/158 (layering + outbox-schema-registration guards) · Startup smoke allowlist 1/1 (workflow in the live dispatch registry).

## Lessons

- **Claim-first dedup is only correct for swallow-style (at-most-once) consumers.** For a retry-for-durability consumer whose effect is already idempotent, a ledger that commits in a separate transaction from the effect is not just useless — it actively suppresses the retry and re-introduces the silent-loss it was meant to prevent. Match the dedup strategy to the delivery semantics, and never let the "shape consistency with other consumers" comment override that.
- **Path-agnostic behavior (a single notification handler over an aggregate event) has a coverage guarantee that per-call-site emission must consciously reproduce.** When replacing it, enumerate every event-raising call site, not just the obvious one.
- **Audit-before-scope keeps paying:** the interceptor path + pre-existing table + pre-registered schema meant the "risky ~1.5d core-handler surgery" collapsed to an additive event + consumer.

## Follow-up filed

`WF-PARALLEL-JOIN-FINAL-NO-STATUS-WRITE` (low) — a FormSubmission workflow terminating via parallel-join-to-final never sets `CompletedAt`, so its status write never fires (pre-existing under both old and new code). Needs a product decision on whether parallel-join represents completion for forms.
