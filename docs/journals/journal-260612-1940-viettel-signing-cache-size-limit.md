# Journal — Viettel signing cache-Set crash (Size required under SizeLimit)

**Date:** 2026-06-12 · **Flow:** `/ck-debug` → fix · **Trigger:** sign smoke "Ký số thất bại — Unexpected error during Viettel Mysign signing" · **Status:** fixed, tested 442 green, reviewed, committed.

## Headline — generic UI error hid a concrete cache-config crash
The FE showed a catch-all "Unexpected error during Viettel Mysign signing". The real exception lived in the API server log:
```
System.InvalidOperationException: Cache entry must specify a value for Size when SizeLimit is set.
  at ViettelMysignLoginClient.GetAccessTokenAsync ...:line 96   ← cache.Set(token)
  at ViettelMysignProvider.SignAsync ...:line 112
```
Login PARSED fine (the expires_in fix worked — Polly logged the login POST as 200) — the crash was the very next line, **caching the token**.

## Root cause
The Api host registers the shared `IMemoryCache` with a **SizeLimit**. Under a SizeLimit, EVERY `cache.Set` MUST specify an entry `Size`; the bare `cache.Set(key, value, TimeSpan)` overload does not → `InvalidOperationException`. Two signing cache writes used the bare overload:
- `ViettelMysignLoginClient:96` (token cache) — the crash site.
- `ViettelMysignCertInfoClient:128` (cert-info cache) — the **next** call after login, same bug → would crash immediately after the login fix. Fixed both (don't whack-a-mole).

The codebase already solved this class everywhere else with `Size = 1` (`MfaService`, `VaultAuditHmacKeyResolver`, `MfaTempTokenService`, `BranchJoinReconcilerJob`); the two Viettel clients were the outliers.

## Why the integration tests missed it
The WireMock signing integration tests (Sync/Poll/Callback, 17 green) exercise the full login→cert-info→sign flow but their `WebAppFixture` `IMemoryCache` has **no SizeLimit** → the bare `Set` succeeds in tests, crashes in the host. Classic test-vs-host config divergence — green tests, broken prod.

## Fix
Both Sets → `cache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = <ttl>, Size = 1 })`. `AbsoluteExpirationRelativeToNow` preserves the exact TTL the old `TimeSpan` overload set. New unit test `ViettelMysignLoginClientCacheTests` uses a `MemoryCache` WITH `SizeLimit` (reproduces the host) + a stub HttpMessageHandler → non-vacuous (fails without the fix), and asserts the 2nd call is served from cache.

## Reusable lessons
- 🔴 **A generic UI error = read the server log for the real exception.** "Unexpected error" is a catch-all; the stack trace (cache.Set → SizeLimit) was the whole answer.
- 🔴 **`cache.Set` under a size-limited `IMemoryCache` MUST pass `Size`.** The bare `(key,value,TimeSpan)` overload throws. Codebase convention here is `Size = 1` (one logical entry).
- 🔴 **Test fixtures that drop a host config (SizeLimit) hide host-only crashes.** The WireMock tests couldn't catch this; the regression guard must replicate the size-limited cache.
- 🔴 **Fix the whole shape:** login + cert-info both had it; fixing only the crash site would have surfaced the cert-info crash on the next attempt.

## Verification
slnx build 0W/0E (`/warnaserror`, NuGetAudit off) · new cache tests 2/2 (size-limited cache) · MysignLoginResponseParsing 3/3 · ViettelMysign integration 17/17 · Identity baseline 251/251. **442 total green.**

## Still open (same arc)
- `VIETTEL-SIGNHASH-STATUS-NUMERIC-RISK` (T3): the signHash/status DTOs (`MysignSignHashResponse.Status` string?, `SignHashClient`/`StatusPollClient` JsonOpts lack `NumberHandling`) remain the next likely number-vs-string trap — the OTP-gated signHash response will reveal it. Reviewer re-flagged with file:line.
