# OrgUnit Tenant Guard & Realtime Broadcast Metrics (T4 FU)

**Date**: 2026-06-20 12:28
**Severity**: Low
**Component**: Identity.UpdateUserCommand, Collaboration.MessageSentSignalRHandler
**Status**: Resolved

## What Happened

Two small follow-up fixes shipped today as a consequence of code-review feedback on earlier ORGUNIT-NOOP and COLLAB-BROADCAST-RESILIENCE fixes. Both were filed as T4 backlogs after Saturday's 49-row audit and picked via `/aqt-next` → `/cook`.

Commit a565ce01: Identity now validates OrgUnit tenant ownership on user update.
Commit 27679918: Collaboration now exports a failure counter for swallowed realtime broadcasts.

## Technical Details

**ORGUNIT slice (a565ce01):**

The prior no-op fix (24d41ecd) made `PrimaryOrgUnitId = cmd.OrgUnitId` reachable — bare Guid? with no FK constraint. A User.Update-permitted caller could now assign a cross-tenant or non-existent OrgUnit, corrupting downstream org-tree scoping.

Guard: `UpdateUserCommandHandler` injects `IOrgUnitModuleClient`, tenant-scoped lookup `GetOrgUnitsByIdsAsync([id], user.TenantId)`. Non-null OrgUnitId must resolve within target user's tenant; 422 ValidationError (no persist) on miss. Null clears the assignment (PUT replace semantics) and skips lookup.

Threat model: system/global user (null TenantId) degrades to existence-only — documented in-handler. Downstream blast radius bounded: `DataScopeService` query-time tenant-scoping prevents foreign id from yielding cross-tenant data access (low-risk).

Implementation detail: `IOrgUnitModuleClient` namespace `SharedKernel.Contracts.Clients` compiles in assembly `AqtCoreFW.OrgUnit.Contracts` — required new ProjectReference on Identity.Application.

Tests: assign-all-fields, tenant-scoped lookup, reject-no-persist, null-skips, not-found. 5/5 green, full coverage of the guard path + edge cases.

**COLLAB slice (27679918):**

Handler already log-and-swallows realtime SignalR faults (best-effort post-persist). Per-message warnings give no aggregate signal to distinguish systemic (schema drift, identity module down) from transient misses.

Added `CollaborationMetrics` static Meter with tenant-tagged counter `collab.signalr_broadcast.failures`. Incremented in the swallow-catch BEFORE warn log. `Counter.Add` is non-throwing — swallow guarantee intact. Registered `.AddMeter("AqtCoreFW.Collaboration")` in OpenTelemetryConfiguration; mirrors WorkflowMetrics/BusinessProcessMetrics pattern.

Prometheus appends conventional `_total` suffix → dashboard `collab_signalr_broadcast_failures_total{tenant=...}`.

Tests: counter-increments-once, OCE-propagates-through. 4/4 green.

## Decision & Trade-offs

- **Validate in HANDLER (async cross-module), not FluentValidation**: sync validators cannot await cross-module lookups; async handler keeps the call ordering clean (lookup after user-fetch).
- **Static Meter for Collaboration**: mirrors modular monolith pattern; zero DI churn. OTel pipeline registration centralized in `OpenTelemetryConfiguration`.
- **Swallow-catch ordering**: increment counter → log. Counter.Add non-throwing guarantees the metric fires even if log throws (low-probability, but correct ordering).

## Verification

Code review: APPROVE_WITH_NITS, zero HIGH/MED blocking.

- Identity.Application.Tests: 5/5 unit green, UpdateUserCommandHandlerTests covers guard path + OCE propagation.
- Collaboration.Infrastructure.Tests: 4/4 green, MessageSentSignalRHandlerTests covers counter-increment + OCE still-propagates.
- Dependency graph: `IOrgUnitModuleClient` registered unconditionally in OrgRegistration.cs:41 + Program.cs:245.
- Public contracts: none changed. Both modifications internal behavior.

## Lesson

**False-green avoidance**: Code-review feedback caught these FU items 24h after the core fixes shipped. Small slices, high-value: tenant guard prevents accidental org-tree corruption; metric counter distinguishes systemic from transient in prod. Neither was missed in initial unit tests because the prior fixes were partial (bare Guid? assignment lacked validation; swallow-catch lacked observability).

**Modular validation seams**: cross-module client injection in command handlers is now a pattern here (identity↔orgunit async lookup). Worth documenting if this grows — async validators in handlers are non-standard FluentValidation, but correct for multitenant domain boundaries.

## Next Steps

1. Both commits ship on main (no merge queue, clean tree).
2. Dependency-map.md updated: Identity.UpdateUserCommandHandler consumer of IOrgUnitModuleClient.
3. Backlog rows closed: T4-ORGUNIT-TENANT-GUARD, T4-COLLAB-BROADCAST-METRIC.

---

**File written**: C:\AqtCoreFW\docs\journals\journal-260620-1216-orgunit-tenant-guard-and-collab-broadcast-metric.md
