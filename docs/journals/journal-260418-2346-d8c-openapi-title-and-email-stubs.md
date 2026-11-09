# D8c: OpenAPI Title Normalization + Email/SMS Test Doubles

**Date:** 2026-04-18 23:05–23:46  
**Severity:** Medium  
**Component:** API Documentation (OpenAPI), Notifications Test Infrastructure  
**Status:** Resolved  
**Version:** v2.118 → v2.119  
**PR:** #44  
**Commit:** bfd0a74d  

## What Shipped

- **OpenAPI Title:** Reverted Vietnamese title `"AqtCoreFW — API Hệ thống Chính phủ số"` to English `"AqtCoreFW API v1"` in `OpenApiVietnameseTransformer.cs:129`
- **Test Doubles:** Introduced `FakeEmailSender.cs` and `FakeSmsProvider.cs` in `tests/shared/AqtCoreFW.Tests.SharedFixtures/`
- **Wiring:** Registered via `WebAppFixture.ConfigureTestServices` (RemoveAll + AddScoped pattern)
- **Unskipped Test:** `NotificationEndpointsTests.SendNotification_EmailChannel_CallsEmailSenderAndPersistsRecord` now green

## Test Results

- Notifications integration: 7/7 pass (email test previously skipped)
- Main integration: 101/101 pass (no regression)
- Code review: 9/10 approved with minor fixes (stale docstring, activation comment) — both fixed in-session

## The Brutal Truth

This PR was supposed to be "quick"—just change a title and stub an SMS provider. Instead, it exposed multiple fragility points in the local dev loop and tooling assumptions that nearly derailed the session.

The most frustrating discovery: **the backlog assumption about orval's filename generation was half-true.** We'd been told that regen with the new title would produce a stable slug; testing confirmed it doesn't reliably. More painful: the first full regeneration attempt pulled in 30 modified endpoint files + cascading downstream changes from 2 weeks of unsynced BE drift. Scope creep disguised as "just regen."

Docker's caching behavior also caught us off-guard—the `compose up -d --build api` command does NOT force-recreate containers, so the old API image kept serving the old title for two retest cycles. This is easily fixable but frustrating when you're already context-switched.

## Technical Details

### OpenAPI Title Decision

**Backlog claim:** Vietnamese title + "v7 produces stable slug"  
**Testing:** Created minimal 3.0.1 spec → got Vietnamese-slug with diacritics.  
**Reality:** Full regen with BE's 3.1.1 spec produced different slug behavior—title alone doesn't predict output; OpenAPI version + operation count + tag structure matter.

**Decision:** Keep English title `"AqtCoreFW API v1"`. Generated filename `aqtCoreFWApiV1.schemas.ts` is stable across old + new title. Side benefit: zero caller churn, cleaner than planned scope.

### Test Infrastructure

**Email:** `FakeEmailSender : IEmailSender { Task SendAsync(...) -> Task.CompletedTask }`  
**SMS:** `FakeSmsProvider : ISmsProvider { Task SendAsync(...) -> Task.CompletedTask }`

Registered in `WebAppFixture.ConfigureTestServices`:
```csharp
services.RemoveAll<IEmailSender>().AddScoped<IEmailSender>(_ => new FakeEmailSender());
services.RemoveAll<ISmsProvider>().AddScoped<ISmsProvider>(_ => new FakeSmsProvider());
```

No tests currently exercise SMS; registered proactively (~10 LOC cost). Email test now runs unskipped.

### Docker Container Lifecycle

**Root cause of first failure:** `docker compose up -d --build api` caches running containers.  
**Workaround:** `stop api → rm -f api → build api → up -d api` forces clean restart.  
**Secondary issue:** `--force-recreate` flag ripples to authserver rebuild, which fails with "/src not found"—avoid it; use targeted `compose build api` instead.

## What We Tried

1. Regenerated OpenAPI client from BE spec → pulled 30 files + unknown downstream impact → reset generated/ to keep PR focused
2. Rebuilding API container via `compose up -d --build` → served old title from cache → explicit container removal required
3. Full `--force-recreate` flag → authserver build failure → narrowed to single-service build strategy

## Root Cause Analysis

1. **Empirical verification gap:** Backlog stored folklore ("v7 stable slug") without testing constraints (OpenAPI version, operation count). Real regen behaves differently than assumed. Future dev: always test with ACTUAL input spec, not minimal synthetic specs.

2. **Docker image caching assumption:** Team assumes `up -d --build` force-recreates; it doesn't. Container lifecycle knowledge incomplete across the team. This will bite again.

3. **Generated code drift:** 2 weeks of unsynced BE endpoints → any client regen tries to pull entire endpoint ecosystem. Single-file scope preservation requires reset, not incremental regen. Trade-off: smaller PR vs. accumulated drift.

## Lessons for Next Time

1. **Verify orval behavior empirically with real OpenAPI specs**, not backlog folklore. Slug generation depends on OpenAPI version + operation metadata, not just title. Create minimal test case first before planning scope.

2. **Docker `up -d --build` does not force-recreate.** Always use explicit `docker compose stop <service> && docker compose rm -f <service> && docker compose build <service> && docker compose up -d <service>` when you need a clean restart. Document this in deployment guide.

3. **Test doubles first, integration later.** Registering SMS before tests need it cost 10 LOC but saved a future PR. Cheap insurance against scope creep when related tests are added.

4. **Generated code scope discipline.** When regen pulls unexpected files, reset to known-good state + document drift separately (separate PR). Mixing title change + endpoint drift muddies code review and rollback risk.

## Next Steps

- Merge PR #44 (code review approved 9/10, minors fixed)
- Update main branch; v2.119 deployed locally
- Defer generated code catch-up (BE drift) to Phase-04 sprint planning
- Document Docker rebuild lifecycle in deployment guide

## Baseline After This Session

Main branch to be updated once PR #44 merges. v2.119 deployed locally. Notifications integration 100% green (101/101 all integration tests pass). OpenAPI title normalized to English per API naming standards. Email + SMS test doubles in place, proactively wired for future notification channel tests.

---

**Status:** DONE  
**Summary:** D8c PR #44 shipped: OpenAPI title normalized, email/SMS test doubles introduced, all integration tests passing, scope kept focused despite generated code drift discovery.
