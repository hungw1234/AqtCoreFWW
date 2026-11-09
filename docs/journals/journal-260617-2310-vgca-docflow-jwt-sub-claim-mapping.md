# DocFlow JWT `sub` Claim Lost to Inbound Claim Mapping — 401 on Every Download

**Date**: 2026-06-17 23:10
**Severity**: High
**Component**: BE Identity / VGCA DocFlow Handler Auth
**Status**: Resolved

## What Happened

5th and final layer of the day's USB-signing chain. After the FileName fix, the desktop SignService's GET finally reached the BE — and got 401. BE log: `DocFlow download: JWT validation failed — "Token sub claim is missing or invalid."`. Browser showed `Connection is closed...` (vgcaplugin.js), which turned out to be the WSS teardown AFTER the 401, not the cause. `/ck-debug` → `/fix`.

## The Brutal Truth

Every prior layer blocked the code path from running, so each fix only exposed the next. The download endpoint had NEVER executed E2E before today — the whole DocFlow handler auth was unverified against a real token. The negative integration tests all passed because the bug returns the SAME 401 they assert, at an earlier gate. Green tests, broken feature, again.

## Technical Details

`DocFlowJwtService` validated with a static `JwtSecurityTokenHandler` left at the .NET default `MapInboundClaims = true`. On `ValidateToken`, that remaps the standard `sub` claim to `ClaimTypes.NameIdentifier`, so `principal.FindFirstValue(JwtRegisteredClaimNames.Sub)` returns null → `Guid.TryParse(null)` → "sub missing". The custom claims `doc_id`/`nonce`/`tenant_id` are absent from the inbound map, so they survived — the failure was specific to `sub`, the fingerprint of the mapping bug.

## Root Cause Analysis

Issuance writes `sub` correctly; validation silently renamed the inbound `sub` to a different claim type, so the lookup by `"sub"` failed. Affects EVERY DocFlow download + upload (both call `TryValidate`), on both the legacy 7-method path and the unified createSigningRequest path — one shared validator.

## The Fix

`new JwtSecurityTokenHandler() { MapInboundClaims = false }` so the validated principal keeps the issued claim names. Issuance is unaffected (claims written with literal short names; outbound map keys on full URIs). Chosen over the "read both sub + NameIdentifier" shim — claim names now match verbatim, single source of truth.

## Lessons Learned

1. **Status-only negative tests are false-green factories.** Four download tests asserted only `401/404/not-OK`; the sub-mapping bug produces exactly those at the JWT gate before reaching the nonce/storage gate they meant to exercise. The one test that would have caught it (a successful 200 download) was deliberately deferred to manual ("requires VGCA service + MinIO") — and manual testing is exactly what exposed it. Pin the REASON, not just the code.
2. **Regression guard at the real boundary.** Added an issue→validate round-trip unit test asserting `sub` reads back as the issued userId (fails with MapInboundClaims=true). Strengthened the exhausted-nonce integration test to assert the 401 body says "Nonce", not "sub claim" — proving the JWT gate was cleared.
3. **"Demo works ⇏ feature works" / unverified seam.** A path never run E2E is unverified no matter how green the unit suite is.

## Next Steps

- **Status Quo**: Commit `ff5937e5` (3 files: fix + unit + integration), BE-only, NOT pushed. 8 commits ahead origin/main.
- **Verification**: unit DocFlowJwtServiceTests 2/2, Identity.Infrastructure 273/273, integration DocFlowHandlerTests 8/8. Code-review APPROVE. Stack restarted with the fix; user E2E retry pending (reload + login after Auth restart, then sign).
- **Follow-up**: a true happy-path E2E download (200 + signed-PDF upload round-trip) still needs MinIO + the desktop tool — currently manual-only; consider a fixture-backed integration test.
