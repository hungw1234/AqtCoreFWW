# BE-LOGIN-USER-ENUMERATION P2 OWASP A04 Fix

**Date:** 2026-05-08 20:30
**Severity:** High (OWASP A04)
**Component:** AuthorizationController.cs (AuthServer)
**Status:** Shipped

## What Happened

Fixed user-enumeration vulnerability in `/account/login` cookie endpoint. Attacker could distinguish valid accounts by observing error message variation:
- L273 (user not found): `"Email hoặc mật khẩu không đúng."`
- L318/323 (wrong password): `"Sai tên đăng nhập hoặc mật khẩu."`

These are semantically identical (both mean wrong creds), but differing Vietnamese text created timing+content oracle.

## The Fix

**AuthorizationController.cs L273:** Unified not-found message to canonical `"Sai tên đăng nhập hoặc mật khẩu."` — now indistinguishable from wrong-password branch.

1-line change. Defensive measure: attacker no longer learns user existence from error text.

## Verification

- AuthServer build: 0E, 0W
- Identity unit: 225/225 PASS
- Architecture: 26/26 PASS
- Code-review: APPROVE 9.5/10 (P3 doc-comment nitpick only)

**Test tightening:** `web/e2e/authserver-login-page.spec.ts` TC-AUTH-021 + TC-AUTH-022 — converted regex-alternation assertions (placeholder pending this fix) to single partial-match `"Sai tên đăng nhập"`. ShowRemainingAttempts policy may append `" Còn lại N lần…"` suffix; partial match tolerates both.

## Scope Boundary (No Changes)

Verified safe, no edits:
- ROPC `password` grant (L124-228): both not-found + invalid-credentials use `Forbid()` without description → OpenIddict default error, no user-enumeration leak.
- Lockout/escalation msgs (L294-297): explicit disclosure required by QĐ 742 1.4.d (intentional security feature).
- Remaining-attempts hint (L314): opt-in per admin policy, trade-off per spec.

## Residual Issues (Backlog)

**BE-LOGIN-TIMING-SIDE-CHANNEL (P3, ~30m):** Not-found branch skips `PasswordSignInAsync` (work-factor ~50-200ms) → measurable timing leak. Fix: dummy hash verification on not-found path to equalize latency.

**BE-OPENIDDICT-LOCKOUT-ERROR-CODE (P3):** ROPC returns `access_denied` for failed credentials/lockout; RFC 6749 §5.2 mandates `invalid_grant`. Separate backlog, untouched.

## Branch & Push

- **Branch:** `fix/be-login-user-enumeration`
- **Commit:** `54d6b839`
- **Status:** Pushed to origin (PR creation deferred to user)

---

**Status:** DONE
