# Audit HMAC-Chain Job → Dedicated `audit` Hangfire Queue + Registration Guard

**Date:** 2026-06-14 · **Type:** fix + test · **Trigger:** `/ck:cook HANGFIRE-AUDIT-QUEUE-ORPHANED`
**Commits:** `0f4b6b16` (fix+guard) · `4f74386d` (docs) — pushed origin (bitbucket 503-pending)

## What

Routed the audit HMAC-chain Hangfire job to its dedicated `audit` queue. It was the **inverse** of the virus-scan-queue bug fixed minutes earlier (a backlog row code-review filed during that fix).

- **Root cause:** `HangfireAuditJobEnqueuer.EnqueueHmacChain` enqueued via `BackgroundJob.Enqueue<HmacChainService>(…)` with no queue arg → the job ran on `default`, even though `opt.Queues` already **listed** `"audit"` as a priority-2 lane. So `"audit"` was orphaned dead config. Mirror image of virus-scan: there the const was used but the server didn't list the queue; here the server lists it but no producer targeted it. Not a stuck-job (default is drained, HMAC chaining still ran) — but the deliberate priority lane (registration comment "audit 2s priority", QĐ742 tamper-evidence) was never realized.

## Decisions

- **WIRE vs DROP (asked the user — genuine fork).** Wire = honor the original design; Drop = YAGNI, remove `"audit"` from `opt.Queues`. User chose **WIRE** — the lane was a deliberate compliance-motivated design, and dropping would *reverse* a prior decision. Surfaced it rather than silently picking the tidier minimalist option.
- **Kept the static `BackgroundJob.Enqueue` call** (did NOT route through `IBackgroundJobService`). `HangfireAuditJobEnqueuer` IS the infrastructure Hangfire adapter behind the audit module's own `IBackgroundAuditJobEnqueuer` abstraction; routing it through the generic abstraction would be adapter-calling-adapter for zero isolation gain. Code-review validated the asymmetry vs the virus-scan path (which *does* use `IBackgroundJobService` correctly, because its caller lives in the Application layer with no Hangfire adapter of its own).

## Fix (4 files)

- `BackgroundJobQueues.Audit` const added.
- `HangfireAuditJobEnqueuer` → `BackgroundJob.Enqueue(BackgroundJobQueues.Audit, …)`.
- `opt.Queues` raw `"audit"` → the const (anti-drift, same as virus-scan).
- **Prevention:** new arch guard `BackgroundJobQueueRegistrationTests` (pure source-text parse, mirrors `HangfireRegistrationConsistencyTests`) asserting every `BackgroundJobQueues` constant is referenced in the server's `opt.Queues`. Structurally prevents the severe stuck-job class (a queue const enqueued to but missing from the server list — the original virus-scan bug). Docstring notes the scope limit: catches constant-based enqueues only, not raw-string ones.

## Verify

API host build `TreatWarningsAsErrors` 0W/0E · new guard 1/1 · Architecture suite 108/109 (the 1 fail is a pre-existing, unrelated MFA compliance test — zero overlap) · Audit.Application 54/54 · existing `HangfireRegistrationConsistencyTests` green · runtime: Hangfire `/hangfire/servers` lists `audit` · code-review APPROVE 0-blocking.

## Lessons

- **Two sibling adapters are not a stack.** `IBackgroundAuditJobEnqueuer` (wraps Hangfire directly) and `IBackgroundJobService` both exist to be the single Hangfire touch-point for their respective callers — don't route one through the other for uniformity's sake. Uniformity isn't always the goal.
- **Spend the prevention guard on the severe class.** Guarding "const must be in the server list" (stuck-job) is worth adding even while fixing the benign inverse (server lists an unused lane). The const-missing case silently runs nothing; the unused-lane case just costs an empty queue.
- **When a "fix" would reverse a deliberate prior design, surface it.** Dropping the audit lane was the minimalist option but would undo a compliance-motivated decision — wire-vs-drop went to the user.
- **3rd Hangfire-queue-routing finding in one chain** (virus-scan const-missing → audit producer-missing → `webhook-normal` unused). Filed `HANGFIRE-WEBHOOK-NORMAL-ORPHANED` (T4) for the third; the guard doesn't catch it (raw-string queue, no const).

**Linked:** `journal-260614-0915-clamav-scan-job-dedicated-hangfire-queue.md` (the virus-scan sibling, immediately prior) · `journal-260614-0845-clamav-managed-stack-dev-scan-activation.md`.
