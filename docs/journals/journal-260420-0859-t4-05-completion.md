---
title: "T4-05 family completion: Phase 02-04 + FU2 shipped with code-review trap discovery"
date: 2026-04-20
session_duration_actual: 3h 22min (parallel delegation reduced from planned 5.5h)
version_bump: v2.120 → v2.121
status: DONE_WITH_CONCERNS
author: claude-code + subagents (fullstack-developer × 3, code-reviewer, docs-manager)
---

# T4-05 Family Completion Session

**Date:** 2026-04-20 (09:09 — 12:31 local)  
**Severity:** High (code-review trap discovered at finalize gate)  
**Component:** Identity module (T4-05 ABAC hardening family + Rules engine cache + MasterData cleanup)  
**Status:** Resolved (M1/M2 fixes integrated before commit)

---

## What Happened

Shipped T4-05 family completion: Phase 02 (dead MasterData consumer removal) + Phase 03 (Rules engine Redis pub/sub cache invalidation) + Phase 04 (integration tests + dependency-map v2.121) + FU2 (ABAC write-path defence-in-depth). Parallel agent delegation compressed planned 5.5h down to 3h 22min actual. Code review flagged Phase 03 wiring gap mid-session (zero callers to `InvalidateCache()` despite shipping infrastructure). Root-cause fix applied before commit + doc drift resolved.

---

## The Brutal Truth

This session embodied the curse of "code compiles ≠ feature works." Phase 03 infrastructure (Redis pub/sub, RulesEngineCacheSubscriber, channel publisher) is **architurally sound and test-verified** — but ships **completely inert in production**. No caller invokes `InvalidateCache()`. The handler `ActivateRuleVersionCommandHandler` that should trigger cache eviction doesn't even know `IRuleEvaluator` exists. We shipped 80 LOC of correct plumbing to a wall that has no door on the other side.

The relief? Code-reviewer caught this at finalize gate, not in prod after 3 weeks. If we'd merged blind, users would see stale compiled rules for the entire 10-minute TTL (vs. sub-second eviction promised in Phase 03 spec).

The anger? **Plan step 1 ("verify caller invokes InvalidateCache") was explicitly skipped.** Agent read the requirement but didn't audit call sites before wiring up pub/sub. This is a pattern I've seen before — when tests pass in isolation, infrastructure work feels "done" without verifying the last mile of integration.

---

## Technical Details

### Phase 02: MasterData Dead Code Removal

```csharp
// Deleted files:
- src/AqtCoreFW.MasterData.Application/Services/DictionaryPublishedEvent.cs
- src/AqtCoreFW.MasterData.Infrastructure/Consumers/DictionaryPublishedCacheInvalidationConsumer.cs
- src/AqtCoreFW.MasterData.Infrastructure/Publishers/DictionaryPublisher.cs

// Modified: src/Program.cs
Line 199-200: removed x.AddConsumers(typeof(MasterDataModule).Assembly) 
                    from MassTransit consumer registration
```

Tests: All 134 MasterData unit tests pass after removing 2-arg ctor call from test fixture.

### Phase 03: Rules Engine Cache Invalidation (The Trap)

```csharp
// Created (pub/sub infrastructure):
- RulesEngineCacheSubscriber : IHostedService (subscribes to Redis "rules:cache-invalidate" channel)
- RulesCacheKeys helper (centralized cache-key + channel naming)
- Publisher: RulesEngineEvaluator.PublishInvalidateMessageAsync()

// The missing wiring:
- IRuleEvaluator interface STILL only has EvaluateAsync() — no InvalidateCache() contract
- ActivateRuleVersionCommandHandler NEVER injects IRuleEvaluator
- ActivateRuleVersionCommandHandler NEVER calls InvalidateCache()
  → Cache remains stale for 10 min; pub/sub never receives an event to relay

// Evidence (Grep "\.InvalidateCache\("):
→ 0 matches
```

### FU2: ABAC Write-Path Defence-in-Depth

```csharp
// Handler guard:
UpdateAbacRuleCommandHandler.cs:25-31
  if (cmd.TenantId != null && rule.TenantId != cmd.TenantId)
    return Result.Fail(ValidationError)

// Repository guard:
AttributeRuleRepository.UpdateAsync()
  if (rule.TenantId != tenantId)
    throw InvalidOperationException  // should never fire, but safety net

// Domain model guard:
AttributeRule.cs
  private set { TenantId = value; }  // reflection tests confirm

// Tests: 9 unit + 3 integration (bonus: added round-trip HostedService test)
→ 248/248 tests pass
```

### Code Review Verdict: 8.5/10 (before fixes) → 9.5/10 (after)

**Major issues caught:**
1. **M1 (Phase 03 wiring gap):** `InvalidateCache()` zero callers — latent infrastructure
2. **M2 (doc drift):** code-standards.md:601 + dependency-map.md:599 reference deleted symbols

---

## What We Tried

1. **Scout (parallel × 2):** Mapped FU2 scope collapse (backlog 2h → actual 65min) + confirmed Phase 02 safe + identified Phase 03 precedent pattern.
2. **Direct Phase 02:** Grep gate (`git grep "DictionaryPublishedCacheInvalidationConsumer"` → 0 hidden refs), delete 2 files, strip from Program.cs, fix 1 test fixture → build clean.
3. **Agent delegation Phase FU2 + 03 + 04:** Handed off to fullstack-developer with scout findings embedded in plan. Agent auto-discovered plan path drifts (Commands/ManageAbacRule vs. Commands/Abac, etc.) and corrected.
4. **Code review revealed M1:** IRuleEvaluator.InvalidateCache has **zero callers**.
5. **M1 fix attempt #1:** Add InvalidateCache to IRuleEvaluator interface + manually wire ActivateRuleVersionCommandHandler.
6. **M1 fix attempt #2:** Re-test after wiring; add unit test asserting NSubstitute Received() call.
7. **M2 doc fixes:** Replace stale code-standards example, update dependency-map narrative.
8. **Finalize:** Run full integration suite (103/103 pass in 6m) + docs-manager sync v2.121 (6 files).

---

## Root Cause Analysis

### Why Phase 03 infrastructure shipped inert:

1. **Plan verification step skipped:** Phase 03 step 1 read "Verify caller invokes InvalidateCache()" — agent read it but didn't actually grep for call sites before shipping.
2. **Tests passed in isolation:** RulesEngineCacheSubscriber + PublishInvalidateMessageAsync round-trip tests all green. When unit/integration tests pass, engineers mentally check "done" without verifying the missing link between subsystems.
3. **Interface mismatch not caught early:** IRuleEvaluator contract never included InvalidateCache, so DI container would reject an attempt to inject and call. This is actually correct defensive design (interface exposes only public API), but it meant the missing wiring was invisible until code-review cross-referenced caller call sites.
4. **Async handler vs sync event:** Original plan discussed MassTransit event-based invalidation. When that pattern shifted to HostedService + Redis pub/sub, the **handler integration point didn't migrate** — old pattern assumed a consumer would auto-subscribe. New pattern needed explicit call.

### Why code-review caught it:

Code-reviewer did the job it should always do but often doesn't: cross-referenced the interface contract against actual usage. Grep confirmed zero callers. This is a **structural pattern inspection**, not a code-style inspection. It requires asking "are we using this?"

---

## Lessons Learned

1. **Plan step execution must be audited, not skipped.** When a plan says "verify X," that's a gate. Treat it like a test assertion: if you skip it, you've introduced unknown risk. For Phase 03, the correct gate was: `Grep "\.InvalidateCache\(" && grep "ActivateRuleVersionCommandHandler.*InvalidateCache" && count == 1`.

2. **Infrastructure code paths are invisible to tests.** Unit/integration tests verify a subsystem in isolation (e.g., "pub/sub message arrives"). They don't verify it's **reachable from production code**. Dead infrastructure passes every test. Future work: add a structural test that scans handler + repository layers and asserts every public method on a cache service is called by at least one production handler.

3. **Interface contracts are integration contracts.** When IRuleEvaluator doesn't expose InvalidateCache, it's not an API design choice — it's a statement: "nothing should call InvalidateCache." If the caller must call it, add it to the interface before implementing the caller. Skipping that step creates structural drift (working code that violates the architecture).

4. **Parallel delegation compresses time but needs tighter definitions.** Delivered 3h 22min vs. 5.5h planned (+60% velocity). But the M1 trap was only caught because code-review was a required finalize gate. If we'd merged without review, the dead infrastructure would ship. Faster ≠ safer without tighter verification gates.

5. **Cache architecture constraints matter.** Phase 03 doesn't use the Identity module's Two-Tier ICacheService (L1 memory + L2 Redis) because RulesEngine compiled LINQ expressions are **not JSON-serializable**. This is a critical design decision that should be in code comments + architecture docs, not just plan files. Future reader will ask "why don't we use ICacheService here?" and without the comment, they might try to refactor incorrectly.

---

## Next Steps

1. **Immediate (done this session):** Wire ActivateRuleVersionCommandHandler → inject IRuleEvaluator + call InvalidateCache after UpdateAsync. Add unit test asserting the call. ✅ DONE
2. **Immediate (done this session):** Fix doc drift: code-standards.md + dependency-map.md. ✅ DONE
3. **Backlog (T4-05-FU3):** Other mutation commands (UpdateRule, CreateDecisionTable, DeleteRule) don't invalidate cache. Only ActivateRuleVersion does. When user edits rule content, compiled engine remains stale until 10-min TTL. Document this trade-off explicitly or extend FU3 to cover all mutations.
4. **Backlog (T4-05-FU4):** Structural test suite to detect dead infrastructure: scan handlers, repositories, services for public/exported methods and assert ≥1 caller in production code paths. Catch similar issues pre-code-review.
5. **Backlog (compliance):** SystemAdmin X-Tenant-Id header override (by-design in B.1) needs audit log differentiation. Currently, ActivateRuleVersionCommandHandler logs the resolved TenantId (from auth context), which hides the fact that a SystemAdmin impersonated a tenant. Low risk (auth policy still enforces Identity.Abac.View), but worth a separate audit entry.

---

## Unresolved Questions / Surface to Backlog

- **Q1:** Should other Rules mutations (CreateRule, UpdateRule, CreateDecisionTable, DeleteRule, etc.) also invalidate cache? Current answer: no, by design (only ActivateRuleVersion does). Risky if user doesn't understand the TTL window. → Backlog as T4-05-FU3.
- **Q2:** Periodic cache refresh (AbacCacheRefreshService, ~1h interval) as safety net for stale compiled rules? Already in backlog but deferred. Completes the "defence-in-depth for 5% residual risk" story.
- **Q3:** Should ICacheService (Two-Tier from Identity) ever support non-serializable payloads? Current answer: no, by design. Serialization is the contract. → Document in architecture decision log.

---

## Closing Reflection

This session was a masterclass in why "tests pass" is necessary but not sufficient. We shipped production-ready infrastructure that will never be called. The real test of any cache-invalidation system isn't whether the plumbing works in isolation — it's whether the system that decides "we need to invalidate" actually knows how to trigger it. Phase 03 failed that test; code-review saved us.

The parallel delegation compressed time impressively (3h vs 5.5h), but it also distributed ownership across 4 agents. When the plan step says "verify," it needs to be someone's explicit responsibility to verify, not just "include it in the plan and hope." Going forward: test infrastructure changes with a "call-site audit" step in the acceptance criteria. Not just "does it compile," but "is it called from exactly these handlers: [list]?"

What's relieving is the catch. What's exhausting is that this was a latent bug hidden inside "code passes tests." Future developers: when code compiles and tests pass but something feels structurally quiet (a pub/sub subscriber with no events, a cache invalidation method with no callers), trust that feeling. It's usually a dead wire.

**Version bumped: v2.120 → v2.121. All 103 integration tests pass. Ready for commit.**

---

## Commit Readiness

- Tests: 103 integration, 248+ unit, 6 Rules integration — all pass
- Code review: 9.5/10 (M1 wiring + M2 docs fixed)
- Docs: dependency-map v2.121, changelog, roadmap, backlog, project-state synced
- Branch: main (no feature branch; direct finalize commits)
- File changes: 2 deleted, 8 created, 12 modified across 4 phases + FU2
