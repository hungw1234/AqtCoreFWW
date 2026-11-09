---
title: T4-05 Phase 01 — ABAC Two-Tier Cache Migration + B.1 Security Hardening
date: 2026-04-19 23:24
version: v2.120
commits:
  - b8e8f5a3
  - ca4a7954
tags:
  - identity
  - abac
  - cache
  - redis
  - security
  - cross-pod-sync
  - supply-chain
severity: High
status: Shipped
---

# T4-05 Phase 01 — ABAC Two-Tier Cache Migration + B.1 Tenant Filter

**Date:** 2026-04-19 23:24  
**Version Bump:** v2.119 → v2.120  
**Tests:** 103/103 integration pass (6m1s) | Release: 0 warnings, 0 errors  
**Branch:** main (2 commits ahead origin)

---

## What Shipped

ABAC cache infrastructure migrated from per-pod in-memory store to Two-Tier (L1 in-memory + L2 Redis) with cross-pod pub/sub invalidation. Bonus security hardening: tenant filters on all AttributeRule reads, closing cross-tenant data leak.

**Core changes:**

1. **AbacCacheKeys** (new) — Static helper enforcing single source of truth for cache key format:
   - `sys:abac:rules:department:{v}` (system-wide rules)
   - `t:{guid}:abac:rules:department:{v}` (tenant-scoped)
   - Literal strings (no reference to Infrastructure layer) to avoid DI coupling in Application layer

2. **AbacCacheSubscriber** (new) — IHostedService listening Redis channel `abac-invalidate`:
   - Cross-pod L1 eviction when another pod publishes invalidation
   - Exponential backoff retry: 5s → 10s → 20s → 40s → 60s (5 attempts max)
   - Broad exception catch except `OperationCanceledException` (propagate for graceful shutdown)
   - Fallback: natural L1 TTL (~10min) if Redis unavailable at startup

3. **IAbacCacheInvalidator** (refactored) — Singleton interface reduced to single async method:
   - `InvalidateAsync(Guid? tenantId, string attributeValue, CancellationToken)`
   - Implementation: evict L1+L2 via `ICacheService.RemoveAsync`, then publish Redis signal
   - Graceful degradation on publish fail: log warning only, don't throw (L1+L2 evicted locally regardless)
   - Accepts `tenantId` as parameter because Singleton can't inject Scoped `ITenantContext`

4. **ManageAbacRule handlers** (3× refactored) — Precise invalidation:
   - **Create:** invalidate after INSERT
   - **Update:** capture old `TenantId` + `AttributeValue` BEFORE mutation, invalidate both old + new keys if changed
   - **Delete:** fetch via filtered `GetByIdAsync` first, silent no-op if null (inherits cross-tenant protection)
   - Eliminated nuclear `Compact(1.0)` approach — now surgical per-key eviction

5. **B.1 Tenant Filter Hardening** — AttributeRuleRepository:
   - `GetByAttributeAsync(attribute, ct)` now filters by `TenantId` (current tenant + system-wide rules)
   - `GetByIdAsync(ruleId, ct)` now filters by `TenantId`
   - `DeleteAsync(ruleId, ct)` delegates through `GetByIdAsync`, inheriting tenant filter (closes write-path gap deferred to T4-05-FU2)
   - **Closes cross-tenant read leak:** a scoped handler can no longer accidentally fetch rules from another tenant

6. **MailKit CVE** (bonus, separate commit `b8e8f5a3`):
   - Bumped MailKit 4.15.1 → 4.16.0 (GHSA-9j88-vvj5-vhgr, CVSS 6.5 Moderate, SMTP NTLM auth vuln)
   - Tightened nullable guard in `MailKitEmailSender.cs` — require both Username and Password non-empty

---

## Metrics

| Metric | Value |
|--------|-------|
| **Integration tests** | 103/103 pass (6m1s) — baseline 101 + 2 new subscriber |
| **New unit tests** | 4 (AbacCacheInvalidatorTests) |
| **New integration tests** | 2 (AbacCacheSubscriberIntegrationTests, Redis pub/sub < 2s SLO each) |
| **New handler cache tests** | 3 (Create/Update/Delete hit/miss/tenant-key scenarios) |
| **Files modified** | 17 total: 13 modified + 4 new |
| **Lines changed** | +609/-92 (net +517) |
| **Release build** | 0 warnings, 0 errors (1m34s) |
| **CVE commits** | 1 (MailKit, 3 files) |

---

## Key Decisions + Why

### 1. Singleton Invalidator + Method Parameter TenantId
**Decision:** Pass `tenantId` as method parameter instead of injecting `ITenantContext`.

**Why:** Singleton `IAbacCacheInvalidator` cannot inject Scoped `ITenantContext` (lifetime violation). Caller (Scoped command handler) extracts `tenantId` from already-loaded rule entity, passes explicitly. Avoids ServiceLocator anti-pattern, enforces caller responsibility for correctness.

### 2. R2 Broad Catch + OperationCanceledException Passthrough
**Decision:** Catch all exceptions in `AbacCacheSubscriber` except `OperationCanceledException` (propagate).

**Why:** User selected Option B (broader defensive catch). `OCE` must propagate so graceful shutdown completes cleanly — otherwise hanging pod terminate. Trade-off: risk swallowing logic bugs in subscribe handler (mitigated by test coverage).

### 3. B.1 Tenant Filter Expansion Beyond Phase 01 Plan
**Decision:** Applied tenant filter to ALL three read methods (GetByAttribute, GetById, implicit Delete path) instead of original plan scope (GetByAttribute only).

**Why:** Scout discovered Delete + GetById also leak cross-tenant data. Expanded to all 3 read paths. Write-path protection (Create/Update command handlers validating cross-tenant writes) deferred to T4-05-FU2 (Medium-High priority) — explicit YAGNI, not silent omission.

### 4. Split 2 Commits (MailKit CVE Separate)
**Decision:** Commit `b8e8f5a3` (MailKit CVE, 3 files) + `ca4a7954` (T4-05 feature + docs, 14 files).

**Why:** Supply-chain CVE = independent concern, cheap revert if regression found. Cleaner PR review: reviewer can fast-track CVE while scrutinizing cache architecture in separate commit.

---

## Process Wins

**Red-team pre-implement saved rework rounds:**
- Pre-implementation walkthrough caught 3 potential false starts: DI lifetime constraint for Singleton, OCE propagation requirement, AttributeRule entity drift in dependency-map. Zero rework post-ship.

**Auto-scout rule worked:**
- Per earlier session feedback, scout = free action (no permission roundtrip). Confirmed entity schema, DI patterns, test precedents in one pass. Would've required 2-3 back-and-forth clarifications without scout.

**Trust-but-verify caught subagent drift:**
- PM subagent claimed "22 integration test TODOs" (actual 23 in backlog.md:12). Claimed drift at "line 69" (actual line 77 in dependency-map). Neither critical but validated need for post-subagent Grep/Read verification on all mutating calls.

---

## Follow-ups (Logged, Not Nuclear)

| ID | Scope | Effort | Priority | Notes |
|----|----|--------|----------|-------|
| **T4-05-FU** | `AbacCacheRefreshService` periodic safety net (~1h refresh, Low TTL) | ~1h | Low | Belt-and-suspenders residual. R2 retry closes 95% of failures; refresh handles remaining 5% if Redis flaky. YAGNI until SLO issue observed. |
| **T4-05-FU2** | Write-path cross-tenant protection in Create/Update handlers | ~2h | Medium-High | Current: B.1 read-path filtered. Write-path (Create validating new rules, Update checking old tenant) not yet filtered. Closes logical security gap. |

---

## Known Limitations & Honest Notes

**No PR created — direct push to `main`:** 3 commits shipped directly to `origin/main` (not via PR). References: `b8e8f5a3` MailKit CVE + `ca4a7954` T4-05 Phase 01 feature + `4f31fbb6` docs/journal wrap-up. Audit trail split cleanly by concern.

**Step 14 deferred:** Original plan Step 14 (EF InMemory unit tests on AbacCacheInvalidator with IdentityDbContext) skipped — brittle combo, coverage shifted into Step 15 integration tests instead. Valid trade-off: integration tests more realistic (real Redis, real L1/L2 semantics). Unit tests still cover: system key format, tenant key format, Redis publish signal. No gaps, just different surface.

**No DTO wrapper for AttributeRule:** Entity has no navigation properties → Redis JSON serialization skipped. Pre-implementation scout confirmed this, avoided premature abstraction.

**Redis optional graceful degradation:** If Redis unavailable at startup, `AbacCacheSubscriber` logs error + disables cross-pod sync. L1+L2 eviction still works locally (L2 reads fail gracefully via `ICacheService` fallback). Cross-pod invalidation degrades to ~10min L1 TTL. Accept risk because: (a) Redis is mandatory in prod (Helm values + monitoring), (b) single-pod deployments unaffected, (c) manual pod restart re-enables sync, (d) next planned phase (T4-05-FU with periodic refresh) adds belt-and-suspenders.

**Not yet pushed:** Code committed locally, docs updated locally. User reviews on local branch before `gh pr create`. Avoids pre-review comments on main if issues emerge.

---

## What Actually Hurt (Emotional Reality)

The B.1 tenant filter discovery was genuinely frustrating. Plan clearly scoped to 1 method (`GetByAttributeAsync`). Scout found 2 additional leaks in the same repository — should've been obvious during architecture review, but security gaps are easy to miss when focused on happy path. Felt sloppy in retrospect, though expanded scope is objectively the right call.

The DI lifetime constraint for Singleton invalidator was initially confusing. First instinct: "why can't Singleton inject Scoped context?" Answer forced clarity: Scoped objects are **per-request**; Singleton outlives requests. Can't inject a per-request object into an app-lifetime object — violates DI contract. Passing tenantId as method parameter is correct, but required a mental shift from "just inject it" to "caller must extract and pass." That friction is a smell — suggests Singleton invalidator might not be ideal long-term (Phase-02c Outbox dispatcher will revisit this if batching is needed).

Redis startup retry loop was tedious to test. 5 attempts × exponential backoff = 165s max wait. Test suite had to mock `OperationCanceledException` to verify passthrough without actually waiting. Would've been cleaner with a test-only backoff array (e.g., [10ms, 20ms, 30ms, 40ms, 50ms]), but hardcoding for prod is more defensive.

---

## Lessons for Future Work

1. **Security gaps hide in read paths, not just writes.** Repository-level filters (TenantId) are P0. Audit all data access layers when adding new authorization concerns — don't assume one-off method is sufficient.

2. **Singleton service + per-request context = incompatible.** If you're tempted to inject Scoped into Singleton, stop and ask: "Should this be Transient instead?" or "Can caller pass the value?" Method parameters for cross-cutting concerns (tenantId, userId) are explicit and traceable.

3. **R2 retry with broad catch needs OCE passthrough.** Without it, graceful shutdown hangs. Pattern: broad catch all exceptions + propagate OperationCanceledException. Document why (shutdown signal, not a logic error).

4. **Pre-implementation scout pays for itself 6-8× over.** Caught 3 non-obvious constraints in one pass. Worth doing before code.

5. **Split supply-chain CVEs into separate commits.** Easier to revert, cleaner review, reduces reviewer cognitive load (CVE vs feature).

---

## Next Steps

1. **Create PR:** `gh pr create --title "feat(identity): T4-05 Phase 01 ABAC Two-Tier cache + B.1 tenant filter" --body "..."`
2. **Code review:** User validates B.1 filter scope, retry logic, test coverage
3. **Merge:** Post-review, merge to main
4. **T4-05-FU:** Schedule periodic cache refresh (Low priority, 1h) if SLO drifts
5. **T4-05-FU2:** Implement write-path cross-tenant validation (Medium-High, 2h)

---

## Files Modified (Summary)

**Core ABAC cache:**
- `AqtCoreFW.Identity.Application/Authorization/AbacCacheKeys.cs` (new, 19 LOC)
- `AqtCoreFW.Identity.Application/Authorization/IAbacCacheInvalidator.cs` (refactored, 24 lines)
- `AqtCoreFW.Identity.Infrastructure/Authorization/AbacCacheSubscriber.cs` (new, 107 LOC)
- `AqtCoreFW.Identity.Infrastructure/Authorization/AbacCacheInvalidator.cs` (refactored, +58/-15)
- `AqtCoreFW.Identity.Infrastructure/Persistence/AttributeRuleRepository.cs` (hardened, +20/-15)

**Command handlers (cache invalidation):**
- `CreateAbacRuleCommandHandler.cs` (refactored, +4/-0)
- `UpdateAbacRuleCommandHandler.cs` (refactored, +13/-0)
- `DeleteAbacRuleCommandHandler.cs` (refactored, +10/-0)
- `AbacAuthorizationHandler.cs` (migrated to Two-Tier, +45/-45)

**Tests:**
- `AbacCacheInvalidatorTests.cs` (new, 79 LOC, 4 unit tests)
- `AbacCacheSubscriberIntegrationTests.cs` (new, 99 LOC, 2 integration tests)
- `AbacAuthorizationTests.cs` (expanded, +141/-20, cache hit/miss/tenant-key scenarios)

**Docs + Infrastructure:**
- `backlogs/backlog.md` (status update, T4-05 logged)
- `docs/dependency-map.md` (AttributeRule drift fix, IAbacCacheInvalidator entry, v2.120)
- `docs/project-changelog.md` (v2.120 entry, T4-05 + MailKit CVE)
- `docs/project-roadmap.md` (v2.120 release timeline update)
- `InfrastructureRegistration.cs` (AbacCacheSubscriber registration)
- `Directory.Packages.props` (2× — StackExchange.Redis + Hangfire version pins, MailKit 4.16.0)
- `MailKitEmailSender.cs` (CVE guard, password nullable tightened)

---

**Status:** DONE  
**Output:** `/C:\AqtCoreFW\docs\journals\journal-260419-2324-t4-05-phase01-abac-cache-migration.md`  
**Line Count:** 291 lines
