# Hangfire Dashboard Relocated from API Host to AuthServer Host

**Date**: 2026-06-14 12:55
**Severity**: Medium
**Component**: Hangfire Integration / AuthServer / SPA Navigation
**Status**: Resolved

## What Happened

The Hangfire dashboard was hosted on the API server (`/hangfire` endpoint), accessible via `[Authorize(AuthenticationSchemes = "Bearer,Cookie")]` MultiAuth. The SPA (React, Bearer-only) tried to navigate to it via external link, which failed silently with a 401 because a browser navigation (anchor tag, no fetch) never attaches the Authorization header — it only sends cookies. The API host receives requests with no auth and no cookie, so the dashboard was unreachable from the browser.

Design assumed two valid solutions: iframe-embed or fix the nav. Both were wrong. iframe-embed is dead (API host sets `X-Frame-Options: DENY` + CSP `frame-ancestors 'none'`), and nav-fix can't turn Bearer into a cookie. The real problem was hosting logic in a Bearer-protected endpoint.

**Fix**: Move the dashboard to the **AuthServer** host, where `HttpContext.User` already contains role claims from the Identity cookie. Filter checks `User.IsInRole("SystemAdmin")` against an actual cookie session. AuthServer stays enqueue-only (no HangfireServer); API host job-processing continues unaffected. Dashboard reads the shared Hangfire SQL store, so the API-host workers still appear in the job inventory.

## The Brutal Truth

This is maddening because the design glossed over a fundamental difference: the SPA is a Single-Page App with Bearer tokens; the Hangfire dashboard is server-rendered HTML that needs a browser session. Putting them both on the API host assumed they could share the same auth boundary. They can't. Bearer tokens don't travel in browser navigations.

The worst part: it "almost worked." In dev, `IsDevelopment()` returned true and skipped auth entirely, so the dashboard opened fine. That masked the architectural mistake until prod-like builds were attempted. A dev-open bypass is a debt, not a feature — it hides the real friction.

## Technical Details

**Root cause**: Bearer auth (stateless token) is fundamentally incompatible with browser navigation. When a user clicks a link, the browser sends cookies, not Authorization headers. The API host, guarded by MultiAuth default to Bearer, has no way to receive the user's identity from a browser nav. AuthServer, guarded by Identity cookie, does.

**What didn't work**:
- iframe-embed: API host explicitly blocks (`X-Frame-Options: DENY`, CSP `frame-ancestors 'none'`). Deliberate clickjacking defense. Correct.
- Nav-fix attempt: Assuming the link was a UI problem, not an auth model problem.

**SameSite=Strict coupling** (red-team finding): AuthServer Identity cookie is `SameSite=Strict` + `__Host-` prefix. Cross-site links (SPA→AuthServer) only carry it if both share an eTLD+1 (registrable domain). Localhost dev masks this because a single host can't have "cross-site." Prod uses `*.aqtcorefw.gov.vn` (same eTLD+1), so the link works. **This is not a bug** — Strict is the hardening. The implicit assumption "move to AuthServer and the cookie magically arrives" is false on separate domains; true on shared eTLD+1. Confirmed post-fix.

**Dashboard mutating-POST CSRF defense is the Strict cookie itself** → don't relax to Lax to "fix" the link. Mutating endpoints on AuthServer are already protected by the fact that a malicious cross-site form post can't send the Strict cookie.

**Filter hardening**: Extracted `public static bool IsAuthorized(ClaimsPrincipal? user)` from `HangfireAuthorizationFilter.OnAuthorizing()`. Unit-testable. Removed the `IsDevelopment()→true` dev-open bypass — SystemAdmin-only in every env.

**Evidence**: 
- Route-probe test: anon `GET /hangfire` on AuthServer returns 401; on API host returns 404 (endpoint gone). Both correct.
- `AuthServerHostFixture` (existing WebApplicationFactory) boots the full Identity pipeline. Regression test renders the dashboard with a SystemAdmin cookie → succeeds. Without the cookie → 401. Green.

## What We Tried

**Option A** (rejected): Keep API host, add a new Identity cookie session on the API host's `/auth` path. Too invasive — API host has no Identity DbContext, no token endpoint. Would require mirroring the entire Identity stack.

**Option B** (chosen): Move dashboard to AuthServer. Minimal scope — wire Hangfire as a direct `HangfireServer` consumer on AuthServer (no enqueue, read-only), repoint the nav link. SPA external-link → `${OIDC_AUTHORITY}/hangfire`.

**Option C** (rejected): iframe-embed with BFF (Backend-for-Frontend) proxy. Extra service, extra routing, extra latency. Fragile compared to a simple hyperlink.

## Root Cause Analysis

Two invisible assumptions:
1. **"The dashboard is just another API endpoint"** — no, it's server-rendered HTML. Endpoint auth ≠ session auth.
2. **"Dev-open bypass is temporary"** — it masks the real architectural issue. Once green in dev, nobody revisits it. The bypass should never exist; real tests should exercise the real auth model from day one.

One code-review catch (Critical, pre-ship):
- Nav entry added only to the hardcoded **fallback menu** in `sidebar-menu.tsx`. Dev runs the **API-driven menu tree**, so the link was dead in dev. All 4 green unit tests rendered the fallback path. Fix: append entry to both paths + add a regression test that exercises the API-driven path with a menuTree fixture. Without that, the test suite was measuring the wrong code path.

## Lessons Learned

**Two flags were never the UI problem — the auth model was.** It's easy to treat a "can't navigate" issue as a nav bug. The real issue was that the target endpoint spoke Bearer, and the browser speaks cookies. No UI patch fixes an incompatible auth model. Dig for the architectural mismatch before tweaking code.

**Red-team found the load-bearing unknown: SameSite=Strict.** The design moved the dashboard to AuthServer and implicitly assumed "the cookie will arrive." True on shared eTLD+1, false on separate domains. Validation was manual TLS harness on a 2-subdomain localhost setup (user-owned, not automated). Couple this with CSRF defense: Strict cookie IS the protection on mutating POST. Don't relax it.

**Automated > manual where a fixture exists.** Red-team flagged the planned "manual smoke" as insufficient. `AuthServerHostFixture` (WebApplicationFactory) already existed — a route-probe (anon `/hangfire` denied + API host 404) proves the dev-open removal automatically. No need for hand-stepped browser smoke.

**Test-covers-wrong-path is silent.** Green tests on the fallback menu meant nothing because dev never ran that code path. All 4 unit tests passed. Code-review caught the dead nav entry by tracing the actual runtime startup. Lesson: trace which code path the test exercises; if it's not the path the user exercises, it's a false-pass.

## Next Steps

1. **Local TLS harness validation** (F1, user-owned, `HANGFIRE-DASHBOARD-F1-MANUAL-SMOKE` T4): Confirm the cookie carries across `sub1.localhost` → `sub2.localhost` with `SameSite=Strict`. Localhost can't exercise this (single host, not cross-site). User to set up 2-subdomain TLS locally or defer to staging.

2. **Deployment sync**: Update deployment-guide to note the AuthServer now hosts the dashboard; API host has no `/hangfire` endpoint. Update SPA `OIDC_AUTHORITY` usage in the nav link.

3. **Code-standards update**: Document session auth vs token auth incompatibility; prefer session-based endpoints on session-only hosts.

**Files changed** (~19): `HangfireAuthorizationFilter.cs` (+unit test), `AuthServer/Program.cs` (register Hangfire, wire filter), `Api/Program.cs` (remove Hangfire, remove dev-open), 3 `InfrastructureRegistration.cs` docstring corrections, `sidebar-menu.tsx` + `admin-menu-entries.ts` (both paths), `sidebar-menu-hangfire-link.test.tsx` (new, API-driven path), `useHangfireNav` hook in test, i18n keys, deployment-guide, README, developer-onboarding, changelog, backlog.

**Commits**: `6c650a3a` (fix), docs inline — origin ahead=0.

**Gates**: filter unit 4/4 · route-probe 2/2 · FE layouts 30/30 (+6 new) · tsc/eslint 0E · AuthServer + API host `/warnaserror` 0W/0E · Phase 1 review APPROVE_WITH_NITS · Phase 2/3 review C1 (Critical nav-dead-code, fixed+tested) → APPROVED.
