# v2.133p3 — AuthServer DI Fix + Conn-String Standardization

**Date**: 2026-04-28 15:56  
**Severity**: Critical  
**Component**: AuthServer (Identity/OpenIddict), AuditService  
**Status**: Resolved  

## What Happened

`/connect/authorize` on AuthServer (port 5002) threw `InvalidOperationException: Unable to resolve service for type 'AqtCoreFW.SharedKernel.Application.IMessageBus' while attempting to activate 'AqtCoreFW.Audit.Infrastructure.Services.AuditService'`. The endpoint was completely blocked. User-reported, caught before release.

## The Brutal Truth

This is a sync-drift bug that should have been caught in v2.132p8, not now in v2.133p3. The real failure point was not the code — it was the process. One host got updated with a new dependency; the other host was skipped because it maintained a parallel manual DI block (forced by connection-string key mismatch). That structural debt is what cost us this 4-hour session to debug and fix.

The frustrating part: `ValidateOnBuild=false` on line 215 of AuthServer/Program.cs (a legitimate decision to avoid loading handlers that need full infra) became a shield that hid the gap. The validation exists for exactly this reason — to catch missing DI — but we disabled it. The first `/connect/authorize` request paid the price.

## Technical Details

**Root cause chain:**

1. **v2.132p8 (ModuleClient Events Extraction Phase 05-06):** `AuditService` constructor got new `IMessageBus messageBus` parameter (line 24 of AuditService.cs). `AqtCoreFW.Api/Program.cs` host got `AddMessageBus(...)` wired. ✓

2. **AuthServer/Program.cs connection-string drift:** Auth host uses `"DefaultConnection"` key; Api uses `"Default"`. This forced AuthServer into a manual Audit DI block (lines 169-183, before the fix) that doesn't call the shared `AddAuditInfrastructure()` helper. That manual block wasn't updated when v2.132p8 landed. ✗

3. **`ValidateOnBuild=false` masked it:** Line 215 disables DI validation, so the missing `IMessageBus` registration stayed invisible until runtime.

4. **First request triggered it:** `/connect/authorize` → OpenIddict flow → MediatR consent handlers possibly invoke `AuditBehavior` → DI container tries to resolve `AuditService` → `IMessageBus` not found → crash.

**Secondary finding (code-reviewer catch):** `AddMessageBus(...)` was initially called WITHOUT a `registerConsumers` callback. With InMemory transport, each login publish would find zero consumers → MassTransit retries 5s/30s/5m per loop → `DeadLetterConsumer` fires after exhaustion. Fixed by registering noop `AuditLogAppendedConsumer` (lines 191-194, only depends on ILogger — safe everywhere).

## What We Tried

1. **First hypothesis:** Missing IAuditService registration. Nope — that was there (line 177).
2. **Second hypothesis:** Missing IAuditLogger. That WAS missing (would have been the next error). Added line 180-181.
3. **Third hypothesis:** The real one. Read the error stack: it's trying to construct AuditService, which needs IMessageBus. Traced to v2.132p8 ModuleClient Events. Grep'd Api/Program.cs → found `AddMessageBus(...)`. Checked AuthServer → not there.
4. **Why was Audit DI manual?** Connection-string key drift. `DefaultConnection` vs `Default`. Eliminated it by standardizing to `Default` everywhere.

## Root Cause Analysis

**Three nested failures:**

1. **Process failure:** v2.132p8 landed a cross-host dependency (IMessageBus on AuditService) but only wired one host. No code review catch; no CI/CD integration test that covers AuthServer /connect/authorize flow end-to-end. ← This is the meta-bug.

2. **Technical debt:** Two hosts, two connection-string keys. That asymmetry forced AuthServer into a maintenance burden (parallel DI block). When the shared code changed, the parallel code didn't.

3. **Validation disabled:** `ValidateOnBuild=false` was necessary for a legitimate reason (Identity.Application handlers need infra not loaded in AuthServer), but it became a footgun. The disabled validation should have been narrower — validate what's needed, lazily load the rest.

## Lessons Learned

**1. `ValidateOnBuild=false` is a tradeoff with real cost.** It lets you ship with missing DI, but first-request-after-deploy surprises become the price. Worth reconsidering when the host has flows that are meant to be used (not just admin utilities). Could explore "validated subset" approach — validate only services actually invoked in this host.

**2. Connection-string key drift multiplies maintenance burden.** Two keys (DefaultConnection, Default) meant AuthServer couldn't use shared DI helpers. When shared code changed, the parallel manual block got out of sync. Eliminating the drift removes the structural reason for future drift.

**3. Fire-and-log publish without a consumer is a slow leak.** AuditService wraps `bus.Publish(...)` in try/catch (lines 58-77). Catches synchronous exceptions. Does NOT catch async retry + dead-letter — that happens out-of-band. With zero consumers registered, InMemory transport wastes 5 minutes of retry threads per login, even though the caller sees success. Fixed by registering the noop consumer so publishes land on a real endpoint.

**4. Cross-host dependency changes need cross-host testing.** v2.132p8 should have had a test that provisions AuthServer AND calls /connect/authorize, not just unit tests on the new consumer. CI/CD integration suite is too late.

## Next Steps

**P1 (deferred, pre-existing):** `messaging.dead_letters` table has ZERO EF migration in any module. Only created via `EnsureCreated` in MessagingFixture.cs (tests). If `DeadLetterConsumer` fires in production (any host), `db.SaveChangesAsync()` will throw `Invalid object name`. This session's fix prevents the trigger in normal flow (noop consumer catches it), but the risk remains for other consumers. Create a proper migration or document the requirement.

**Code Review Approval:** 0 P0 issues. Commit `53995fc7` pushed to origin/main.

## Verification

- `dotnet build src/AqtCoreFW.slnx`: **0E/0W** (1m13s)
- `dotnet test tests/unit/AqtCoreFW.AuthServer.Tests`: **14/14 PASS**
- `dotnet test tests/unit/AqtCoreFW.Audit.Infrastructure.Tests`: **18/18 PASS**
- Code-reviewer: **APPROVED**
- Git: Commit `53995fc7` on main, origin/main synced

## Files Changed

**Core fix (AuthServer/Program.cs):**
- Line 5: Added `using AqtCoreFW.Infrastructure.Messaging;`
- Line 7: Added `using MassTransit;`
- Line 24: Switched conn-string key `"DefaultConnection"` → `"Default"`
- Lines 180-181: Added `IAuditLogger → AuditService` map
- Lines 185-194: Added `AddMessageBus(...)` with consumer registration callback (comments explain the InMemory + noop consumer pattern)

**Standardization (conn-string key):**
- `src/host/AqtCoreFW.AuthServer/appsettings.json`: `DefaultConnection` → `Default`
- `src/host/AqtCoreFW.AuthServer/appsettings.Development.json`: `DefaultConnection` → `Default`
- `src/host/AqtCoreFW.AuthServer/appsettings.Production.json`: `DefaultConnection` → `Default`
- `infra/docker/docker-compose.yml:49`: Env var `DefaultConnection` → `Default`
- `infra/helm/authserver/values.yaml:69`: Env var `DefaultConnection` → `Default`

**Fail-fast hardening (MessagingRegistration.cs):**
- Lines 29-33: Replaced `?? string.Empty` fallback with explicit `throw new InvalidOperationException(...)`. Prevents latent NullReferenceException in DeadLetterConsumer at 3am during retry exhaustion.

---

**Status:** ✅ DONE
