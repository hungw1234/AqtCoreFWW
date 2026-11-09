# BP MVP P0 OTel-only SHIPPED — v2.137p22

**When:** 2026-05-14 ~17:00 ICT (session ~25 min, 21st ship of day)
**Commits:** `540e235f` feat + `8d6c3f8c` docs → origin/main
**Plan:** `plans/260420-1957-business-process-definition-mvp/phase-00-workflow-perf-otel-hardening.md`

## Session goal

Ship BP MVP P0 in the fatigue window. /aqt-next recommended smallest validated scope after 20 ships earlier in the day; P0 scope had been frozen to OTel-only via two validation passes (D4, D9, D10) — no migration, no index, no schema change.

## What worked

- **Static Meter pattern** mirroring `OpenTelemetryConfiguration.AqtSource` static `ActivitySource`. Zero DI churn, zero handler-ctor change. `WorkflowMetrics` exposes 2 histograms + Meter name constant.
- **`HandleCore` extraction** + `try/finally`: histogram emits on every exit path, including early-fail and exceptions. Cleaner than weaving Stopwatch into 14-step handler.
- **Tag-source asymmetry handled explicitly:** Command (`ExecuteTransitionCommand`) has no `TenantId` field → callback `Action<Guid,string> setTags` invoked after instance+transition load; query (`GetAvailableTransitionsQuery`) has `TenantId` directly → no callback needed.
- **AddMeter wiring trivial:** one line in `OpenTelemetryConfiguration.WithMetrics`. Existing OTLP exporter at line 46 picks it up automatically.
- **Build + tests clean:** 0E/0W, Workflow.App 431/431, Workflow.Int 22/22 (2 skip), Architecture 97/97.
- **Code review 9.5/10**, 0 Critical/High/Med. 3 Low FU candidates surfaced (early-fail placeholder tags, no `result` tag on query, cancellation→fail) — all explicitly accepted per plan scope; **NOT** filed as FU rows (YAGNI).

## What didn't / lessons

- **First-try compile fail:** Initial Edit assumed `ExecuteTransitionCommand.TenantId` existed (mirroring the query). Command doesn't carry tenant — it lives on the aggregate. Caught immediately by re-reading the command file before build; switched to callback pattern. **Lesson:** read the command record before touching the handler when adding cross-cutting telemetry; commands and queries often have asymmetric ambient-context shapes.
- **Tool reminders surfaced 3× during straightforward work** — would have been noise without focused Task tracking. Stayed disciplined: 7 tasks created upfront, marked progress as each completed.

## Decisions made

- **D-Inline-1: Tag-source split is intentional.** Command uses callback (tags resolved after load), query uses request field directly. Considered uniformizing to callback-everywhere — rejected as artificial complexity. **Why:** the asymmetry reflects real data location (command tenant comes from aggregate, query tenant comes from request).
- **D-Inline-2: No new tests for telemetry plumbing.** 453 handler tests already exercise both wrapped paths; telemetry is additive and uses well-tested OTel APIs. Adding histogram-emission assertions = test-side complexity for low value (would just wrap `Meter.CreateMeterListener` — fragile). Accepted reviewer's confirmation.
- **D-Inline-3: 3 Low FU candidates dismissed at ship.** Early-fail placeholder tags, no `result` tag on query, cancellation→fail = all aesthetic/dashboard-side concerns that surface only after baseline data exists. File only if real dashboard pain emerges. **Why:** preserves YAGNI; avoids backlog drift accumulation.

## Test status

| Suite | Result | Time |
|-------|--------|------|
| `dotnet build src/AqtCoreFW.slnx` | 0E / 0W | 2:35 |
| Workflow.Application.Tests | 431 / 431 | 1s |
| Workflow.Integration.Tests | 22 / 22 (2 skip) | 54s |
| Architecture.Tests | 97 / 97 | 25s |

Zero regression. P0 is observable-by-construction once OTLP collector is live.

## Pattern codified — command-vs-query tag-source asymmetry

When adding cross-cutting telemetry (tags, audit, tracing) to MediatR handlers:

1. **Check the request shape first.** Query requests typically carry tenant/user/correlation ids; command requests often don't (they rely on aggregates or ambient claims).
2. **Don't force uniformity.** If a tag is on the request → bind it directly. If a tag is on the aggregate → use a `setTags` callback fired after load.
3. **Emit on every exit path.** `try/finally` around an extracted `HandleCore` method, not around individual return statements.
4. **Best-effort defaults** for tags that resolve mid-handler: `Guid.Empty` for ids, `"unknown"` for strings. Inline-doc the placeholder semantics so dashboard authors filter them.

## Next session

- **P1** FormTemplateVersion entity + Publish atomic snapshot (1.5d, ready, parallel-safe to P2)
- **P2** FormSubmission `WorkflowInstanceId`/`WorkflowStateId` linkage + migration + factory + `IFormSubmissionRunClient` (0.5d, pending → unblocks P3→P4 chain)
- **P3** BP module scaffold + RLS + migration (1d, blocked on P2)
- Optional ops smoke: verify OTel emission to local OTLP collector once dev-stack runs.

Tail of `journal-260514-1612-bp-p0-scout-otel-only.md` and `journal-260514-1522-bp-mvp-validate-plus-p4-fu.md`.
