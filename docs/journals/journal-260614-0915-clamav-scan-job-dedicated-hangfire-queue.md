# ClamAV Scan Job Routed to Dedicated Hangfire Queue

**Date**: 2026-06-14 09:15
**Severity**: Medium
**Component**: Background Jobs / ClamAV Integration
**Status**: Resolved

## What Happened

`UploadFileCommandHandler` enqueues ClamAV scan jobs via `IBackgroundJobService.Enqueue()`, which had no queue parameter. Jobs defaulted to Hangfire's `default` queue despite doc-comments claiming a `virus-scan` queue existed. The server-side Hangfire registration never registered `virus-scan` in `opt.Queues`, so even if a job explicitly targeted it, the processing server would ignore it. Latent failure: a file uploaded now gets scanned (the `default` queue runs), so the issue wasn't visible to users, but a downstream refactor isolating virus-scan to a dedicated queue would silently strand jobs in the non-existent lane.

## The Brutal Truth

This is infuriating because it's a classic invisible coupling bug — the doc lies, the code works, and nobody notices until operational needs change. The real kick in the teeth: when you DO add the `virus-scan` queue to prod infrastructure thinking it's ready to drain, jobs enqueued without a queue parameter keep hitting `default` instead. The queue sits empty, the job logic sits idle on the wrong lane, and debugging reveals the producer and consumer never agreed on the contract.

## Technical Details

**Root cause**: `IBackgroundJobService.Enqueue(Expression)` overload in Files.Application had no queue routing. It called `BackgroundJob.Enqueue(...)` which Hangfire defaults to `"default"`. No compile-time check, no explicit queue name in the job metadata.

**Silent coupling failure**: Hangfire jobs enqueued to a queue name NOT in the server's `opt.Queues` are never picked up. A job enqueued to `"virus-scan"` on a server configured with `opt.Queues = ["default"]` will sit in the queue table forever, untouched.

**Evidence**: Post-fix, Hangfire dashboard `/hangfire/servers` now lists `virus-scan` among drained queues. Pre-fix, only `default` appeared. API warnaserror build 0W/0E; Files.Application 114/114 green; runtime queue registration verified.

## What We Tried

Considered: `[Queue("virus-scan")]` attribute on `IClamAvScanJob` interface (Hangfire's idiomatic approach). Rejected: the interface lives in Files.Application, which has no Hangfire reference. Adding the attribute would couple the application layer to Hangfire and break the module abstraction boundary. WebhookDispatcher precedent showed the correct pattern: enqueue via explicit queue parameter on the service interface.

## Root Cause Analysis

Two separate decisions, each invisible to the other:
1. **Enqueue side**: handler calls `IBackgroundJobService.Enqueue()` (no queue param) → defaults to `default` queue.
2. **Server side**: Hangfire registration never registered a `virus-scan` queue in `opt.Queues`.

Neither layer validates that the other is ready. A doc comment claiming a `virus-scan` queue existed made it even more insidious — the lie was written down, so it *should* be there.

## Lessons Learned

**Shared constants prevent silent runtime mismatches.** Introduced `BackgroundJobQueues.VirusScan` constant used by BOTH the enqueue call site (UploadFileCommandHandler) AND the server registration (HangfireRegistration). A typo in one place now fails the build instead of failing at runtime. This is the one-time investment that turns a 4-hour debugging session into a compile error.

**Respect abstraction boundaries over framework idioms.** Hangfire's `[Queue]` attribute is idiomatic and clean, but it doesn't belong in a framework-agnostic application layer. The service interface is the abstraction — route through it, not through infrastructure-specific attributes.

**A running host locking its bin is not a compile error.** Hit MSB3027 "file in use" mid-fix on AuthServer — the running API had api.dll locked. Not a code problem, a host problem. Stop the host, rebuild, then reason about the actual compiler output.

## Next Steps

1. **Verify end-to-end**: Restart API and confirm Hangfire dashboard shows the `virus-scan` queue drained by at least one worker.
2. **Cleanup**: Mark `HANGFIRE-AUDIT-QUEUE-ORPHANED` (T4) — the `audit` queue is registered but its enqueuer also defaults to `default` (inverse symptom, same root class).
3. **Document**: Queue naming convention and the shared-constant pattern in code-standards if not already present.

**Files changed**: `BackgroundJobQueues.cs` (new), `IBackgroundJobService` (+overload), `HangfireBackgroundJobService` (impl), `HangfireRegistration` (opt.Queues config), `UploadFileCommandHandler` (call site), `UploadFileCommandHandlerTests` (assert constant not literal).

**Commits pushed**: `03238fce` (fix), `a4eb66df` (docs) — origin ahead=0, bitbucket 503-pending.
