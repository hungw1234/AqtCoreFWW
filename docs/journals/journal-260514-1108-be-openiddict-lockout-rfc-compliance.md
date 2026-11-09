# BE OpenIddict Lockout — RFC 6749 Error Code Compliance

**Date:** 2026-05-14 10:50–11:08 ICT
**Commits:** 1 → origin/main `c0ec5b3a`
**Range:** `5143cf12..c0ec5b3a`
**Branch:** main (fast-forward, no divergence)
**Type:** fix(auth) — error-code compliance

## Session Goal

Close backlog row `BE-OPENIDDICT-LOCKOUT-ERROR-CODE` (P3, RFC 6749 §5.2 violation in ROPC lockout path). Implement /cook flow: scout → plan-with-tradeoffs → user-approve → implement → test → review → journal.

## What Happened

ROPC (Resource Owner Password Credentials) grant emitted `access_denied` on account lockout. RFC 6749 §5.2 specifies `access_denied` belongs to **authorization endpoint** flow (§4.1.2.1), not token endpoint. Token endpoint errors MUST be §5.2 codes: `invalid_request`, `invalid_client`, `invalid_grant`, `unauthorized_client`, `unsupported_grant_type`, `invalid_scope`. Sibling ROPC paths (user not found, wrong password) already returned bare `Forbid()` → framework default `invalid_grant` (compliant). Lockout path was the outlier, manually set to `access_denied` (non-compliant).

**One-line fix:** swap `"access_denied"` → drop custom, rely on default `Forbid()` → `"invalid_grant"`.

## The Brutal Truth

Thirty minutes for **one line of BE code** feels absurdly expensive. But the process uncovered why: the cost is actually 90% test tightening + coordinate-across-specs validation, 10% source change. Tester did a proactive grep sweep across both E2E specs to hunt for hardcoded `"access_denied"` lockout assertions—found two (TC-AUTH-055 + TC-AUTH-024), had to flatten nesting, yank stale comments. Code review flagged the RFC comment was borderline (accurate but could be seen as over-explaining). These are not bugs, just scaffolding. The actual git diff is 5 lines.

Reality: **complexity is invisible until you touch it.** What looked like a 15-minute backlog row expanded to 30min wall-clock because the spec coverage was scattered, and we were right to tighten it.

## Technical Details

**BE change:** `AuthorizationController.cs:159`
```csharp
// Before: return Forbid("access_denied");
// After:  return Forbid(); // RFC 6749 §5.2: token endpoint uses invalid_grant, not access_denied
```

**Test assertions tightened:**
- `authserver-token-grants.spec.ts` TC-AUTH-055 (L97–104): nested `if (lockoutReason)` flattened; `expect` block now inline; stale "todo: verify" comment removed.
- `authserver-login-page.spec.ts` TC-AUTH-024 (L106–108): assertion switched from `lastStatus === 401` (historical artifact) to `errorCode === "invalid_grant"`; commented-out 401 left as defense against OpenIddict drift.

**Grep sweep result:** 0 hidden lockout assertions. Tester verified no other E2E specs or BE unit tests hardcoded `access_denied` in lockout context — all clean.

## What We Tried

**Plan trade-offs presented to user:**
| Option | Approach | Cost | Risk | Chosen? |
|--------|----------|------|------|---------|
| A | Manual string search + test fixup | <1h | None (simple, surgical) | ✓ |
| B | Full ROPC error-code audit (7 paths) | 4–6h | Scope creep; other paths already compliant | ✗ |
| C | Add RFC comment + pinned test assertion | +30min | Maintenance burden if OpenIddict updates | ✓ |

User approved Option A+C in one keystroke.

## Root Cause Analysis

**Why did this slip?**
1. Lockout path was added late in QD742 Phase-03 (mid-Apr). Other ROPC paths were already test-covered with `Forbid()` default; lockout got manual `"access_denied"` without cross-spec audit.
2. RFC compliance isn't automated — no linting rule or architecture test flags non-compliant error codes. Added to code-standards.md as a checklist item, but not enforced at build time.
3. Error-code constants live in OpenIddict library; bare `Forbid()` calls the framework default. When you supply a string, you override that default — no warning.

## Lessons Learned

**Lesson 1: Scout precision pays 10× at implementation time.**
The backlog row cited the exact file:line (`AuthorizationController.cs:159`) and the exact constant being violated (`access_denied`). Scout agent returned a surgical file diff within 2 minutes. Compare to bucket-classifier scouts in earlier sessions (PHASE7-TIER-D, QD742-PHASE04) which produced wrong hypotheses. **Invest 2 minutes in backlog-row scope precision; pays off massively.**

**Lesson 2: Error-code disclosure is orthogonal to compliance.**
The string returned to the client ("Account locked. Try again later.") is the actual user-enumeration leak. The error CODE (`access_denied` vs `invalid_grant`) is separate. Changing the code does NOT change the leak vector. Future: when RFC-complying on OAuth2/OIDC errors, audit description text independently. (Deferred as separate UX decision: bare `Forbid()` reveals less, but "locked" is better UX than "invalid credentials" — user knows to wait. Out of scope for P3 compliance fix.)

**Lesson 3: Plan-with-table + scope-boundary → single-character approval.**
Presenting explicit file diffs + trade-off table + "what we're NOT touching (YAGNI)" section converted potential 2-line approval chat into `y`. Worth repeating.

## Validation

- **Compile:** 0E/0W (dotnet build AuthServer).
- **FE typecheck:** clean.
- **Identity.Integration:** 198/198 PASS (2m 43s).
- **Architecture:** 97/97 PASS (19s).
- **Grep sweep:** 0 residual hardcoded `access_denied` lockout assertions in BE or FE.
- **Code review:** APPROVE 9.7/10. Two nits accepted: (1) RFC comment is borderline-justified (trade-off: pin the rule in code vs. assume future maintainer will know RFC 6749 §5.2 off-hand—we kept the comment); (2) technically-dead `lastStatus === 401` branch in TC-AUTH-024 left as forward defense against OpenIddict status-code drift (small maintenance cost, nonzero value).

## Files Changed

1. `src/host/AqtCoreFW.AuthServer/Controllers/AuthorizationController.cs` — 1-line constant swap + 1-line RFC pin comment (L159–161).
2. `web/e2e/authserver-token-grants.spec.ts` — assertion tightening + nesting flatten + stale comment removal (L97–104).
3. `web/e2e/authserver-login-page.spec.ts` — assertion swap + defensive comment (L106–108).
4. `backlogs/backlog.md` — RESOLVED marker inline, no move to completed.md per project convention.
5. `docs/project-changelog.md` — v2.137p17 entry (L9–24).

## Process Cleanup

- 3 agents used: scout (Explore) → planner → implementer/tester/reviewer (sequential via /cook, parallel via project-manager).
- Scout precision was unusually high; no bucket-classifier false trails.
- Tester proactively grepped beyond scope (good behavior).
- Code review accepted defensible nits; no correctness issues.

## Next Session

Carry-over (filed separately, low priority):
1. **Description-text disclosure UX tradeoff** (`"Account locked"` vs bare `Forbid()` returning opaque `"invalid_grant"`). Separate decision; current hardening is compliant. Sub-millisecond user enumeration risk remains theoretical.
2. **RFC compliance checklist in code-standards.md** — added item L2040-2050; could escalate to automated guard if cadence grows (e.g., scan all error returns in auth controllers at build time).
3. **i18n sweep** — verify no orphan `"Account locked"` string keys in `locales/vi.json` after this fix (likely none, but worth confirming).

## Dispatch Ops

- **Branch:** main (all commits fast-forwarded, no conflicts).
- **Status:** DONE (all validation green, review approved, journal filed).
- **Git state:** Clean, no untracked files, ready for next session.

---

**Status:** DONE
**Journal path:** `C:\AqtCoreFW\docs\journals\journal-260514-1108-be-openiddict-lockout-rfc-compliance.md` (75 lines)
