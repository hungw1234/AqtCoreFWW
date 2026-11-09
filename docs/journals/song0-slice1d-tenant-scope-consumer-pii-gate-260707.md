# Sóng-0 Phase-1 Slice 1D — Tenant-Scoped Consumer + PII-Gate Coverage

**Date**: 2026-07-07 14:30 Asia/Saigon
**Severity**: High
**Component**: Outbox consumers, tenant context, PII masking
**Status**: DONE

## What Happened

MassTransit background consumers run outside HttpContext, which means SESSION_CONTEXT is unset when they invoke tenant-scoped EF queries. Current code "works" only because runtime login is sa/db_owner, which bypasses RLS filters. Slice 1D locks tenant derivation from event payload + wires tenant scope into consumer pipeline to prevent least-priv breach. Parallel: PII-gate coverage expands to scan all SharedKernel.Contracts for ID-only fields (currently partial, blocked by AUDIT-ACCOUNTDISABLED-EVENT pre-existing contradiction).

## The Brutal Truth

This is incredibly frustrating because the bug is architecturally invisible under db_owner. All our integration tests pass, SESSION_CONTEXT reads correctly, but the moment we run with a real low-priv service account, tenant-scoped writes fail silently or RLS blocks them entirely. We shipped 1C (consumer dedup) without this tenant-scope layer, leaving the door open for a low-priv deployment to break. The real kick in the teeth is that code-reviewer caught a false-green wiring gap: unit tests (mock IPipe) + read-back tests (hand-rolled ambient.Push) both BYPASS the actual `UseConsumeFilter` registration, so we could delete the wiring line and tests would still pass. This is a repeat failure mode — last 6 months saw 3× false-green wiring.

## Technical Details

**Design: AsyncLocal + consume-filter for DRY tenant scoping**
- `ITenantScopedIntegrationEvent` marker interface on payload
- `AmbientTenantScope` (AsyncLocal<Guid?>) stores payload tenant during consumer dispatch
- `ScopedTenantContext` factory checks ambient first (before HttpContext/Hangfire fallback) → additive, not override
- `TenantScopeConsumeFilter<T>` middleware (MassTransit): derives tenant from `event.TenantId` (cast T → ITenantScopedIntegrationEvent) → calls `ambient.Push(tenantId)` → block runs → Pop
- Wired: `cfg.UseConsumeFilter(typeof(TenantScopeConsumeFilter<>), InMemory|RabbitMQ)`

**PII-gate scan coverage**
- Expanded SharedKernel.Contracts to detect nullable fields + unwrap (UserName, Email, PhoneNumber patterns)
- Scanner hits 47 contract types; 2 fields pre-existing "NO PII in event" comment contradicted by actual payload (AccountAutoDisabled carries UserName email, pre-existing FU: `AUDIT-ACCOUNTDISABLED-EVENT-PII-COMMENT-CONTRADICTION`)
- IDs-only enforcement: 11 form-integration + 34 audit-integration consumers verified zero PII-at-rest

**Test wiring false-green catch (code-reviewer M1)**
- Unit test mocks IPipe → filter middleware never invoked → test passes even if `UseConsumeFilter` line deleted
- Read-back test (hand-roll ambient.Push) → manually sets ambient, bypasses registration validation
- **Fixed:** bus-harness test drives filter via MassTransit test harness (`BusTestHarness`), validates filter runs on actual registered pipeline
- 12 new SharedKernel unit tests (scope + filter + bus-harness)

**Verification matrix**
- Contracts 47/47 scanned
- SESSION_CONTEXT read-back (Notifications.Integration 11/11, Forms.Integration 34/34)
- Architecture tests 150/150
- 1 pre-existing fail: `SourceLevelRulesTests.Rule1_GuidNewGuid` (signing/files, unrelated)

## What We Tried

Rejected: per-consumer explicit tenant-set. Would work but scattered tenant derivation across 11+ consumer classes → high maintenance, DRY violation, easy to miss on new consumers.

Rejected: HttpContext fallback + assume consumers always inherit tenant. Consumers run outside HTTP pipeline in Hangfire/RabbitMQ → HttpContext is null → either crash or default to Guid.Empty (wrong tenant).

Rejected: PII-gate defer to Phase-2. User (product stakeholder) chose full-plan-1D to close C11 architecturally (tenant + RLS integrity) instead of YAGNI-scoped defer.

## Root Cause Analysis

The root is that SESSION_CONTEXT is a request-scoped container (HttpContext-bound or explicit Hangfire context). Consumers in background queues have neither, so queries run as "unscoped" tenant (RLS sees no filter). The fix is to push tenant context into AsyncLocal at the message-dispatch boundary (filter), where we have the payload, before EF DbContext retrieval. The false-green pattern (tests bypass registration) is a test-harness gap — unit/read-back don't validate wiring, only mock logic.

## Lessons Learned

1. **Background consumers need explicit scope push.** HttpContext fallback is not sufficient. Tenant derivation MUST happen at dispatch boundary, keyed on payload ITenantScopedIntegrationEvent, not request state.

2. **Test registration separately from logic.** Unit tests that mock the container (IPipe, DbContext) cannot catch missing or deleted pipeline middleware. Add bus-harness tests that drive the real registration path.

3. **RLS block enforcement is untestable under db_owner.** SESSION_CONTEXT read-back confirms scope set, but RLS filters are bypassed at sa login. True validation requires non-db_owner test harness (accepted debt, tracked separately).

4. **Pre-existing PII contradictions are architectural, not copy errors.** AUDIT-ACCOUNTDISABLED event mixes "NO PII" comment with actual UserName/Email payload. This is a contract-definition flaw, not a code bug. Requires product decision: strip payload (breaking change) or update comment (accept PII-at-rest for account-disable event).

## Next Steps

1. **RLS-block enforcement debt.** Tracking as FU: `AUDIT-ACCOUNTDISABLED-EVENT-PII-COMMENT-CONTRADICTION` + RLS true-validation harness (Phase-2). Current integration tests cannot assert RLS blocks because sa bypasses.

2. **Verify non-tenant-scoped publishers.** Scout found 3/4 publishers via outbox (1D does NOT convert publisher, LockoutEscalation still direct). Docs claim "4/4 via outbox" → update backlog/roadmap to reflect phase-complete state.

3. **Staging chaos test.** Simulate tenant-agnostic lookup (e.g., consumer queries `_context.Users.FirstAsync()` with no scope) → confirm RLS blocks with proper log message (once non-db_owner harness ready).

---

**Status:** DONE  
**Summary:** Locked tenant-scope derivation from outbox consumer payloads via AsyncLocal + consume-filter middleware; caught false-green wiring gap (unit tests bypass registration); expanded PII-gate to 47 contracts; deferred RLS-block enforcement testing to Phase-2 due to db_owner bypass.  
**Journal:** `C:\AqtCoreFW\docs\journals\song0-slice1d-tenant-scope-consumer-pii-gate-260707.md`
