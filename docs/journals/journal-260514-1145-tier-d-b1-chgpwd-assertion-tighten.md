# Tier-D B1 Change-Password Assertion Tighten

**Date:** 2026-05-14 11:45 ICT
**Commit:** `49bcf4ea` on `origin/main`
**Type:** test fix (Playwright expectation)
**Backlog Row:** PHASE7-TIER-D-B1-CHGPWD-ASSERT (P3, ~10 min wall-clock)

## What Happened

TC-IDENT-034 "Change password e2e" expectation was overly permissive: `expect(response.status()).toBeIn([200, 400, 422, 500])` accepted 500 Internal Server Error as valid outcome.

Root cause: obsolete comment "(×2 specs)" — backlog row written with assumption 2 status paths needed guard. Sibling tests cleaned ~1 week ago; only 1 occurrence remained, untracked.

## The Fix

**File:** `web/e2e/account-change-password.spec.ts:24-25`
- Assertion: `[200, 400, 422, 500]` → `[200, 400, 422]`
- Dropped comment "500 = tenant context missing"
- **Rationale:** 500 means API crashed; valid spec outcomes are 200 (success), 400 (bad request), 422 (validation failed). Tenant context always present in E2E seeded fixture.

**Related files:**
- `backlogs/backlog.md` — resolved inline
- `docs/project-changelog.md` — v2.137p19 entry

## Validation

- FE tsc: 0 errors
- Code-review: APPROVE 9.8/10 (0 critical/high/medium)
- **Pattern:** Backlog row drifts ~1 day when prior fixes incomplete (sibling cleanup unlinked)

## Lesson Learned

**`/cook --auto P3` without row name → needs UI decision gate.** `--auto` is for workflow gates (build/test), not backlog pick decisions. Solution: when row name missing, trigger candidate-picker listing 3 sealed-scope options (blast-radius ascending).

**Status:** DONE

**Output file:** C:\AqtCoreFW\docs\journals\journal-260514-1145-tier-d-b1-chgpwd-assertion-tighten.md (36 lines)
