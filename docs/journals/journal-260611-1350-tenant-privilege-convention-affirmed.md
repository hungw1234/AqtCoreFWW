# Journal — Tenant Privilege Convention Affirmed (2026-06-11 ~13:50)

**Commit:** `6ac62223` (NOT pushed — user deferred push timing)

## Session goal
`/aqt-next` flagged open-Q `EXTID-LOGINAUDIT-PRIV-CONVENTION` from the morning tenant-isolation fix: why do ExternalIdentities + LoginAudit gate on `SystemAdmin` only, while Delegation gates on `SystemAdmin-OR-Admin`? Audit this apparent inconsistency before shipping.

## What worked
- **Semantic false-alarm verdict**: The two `IsPrivileged` flags gate DIFFERENT operations, not the same one. No bug, not a regression.
  - **Tier 1 — cross-tenant escape** (bypass tenant isolation, see ALL tenants): SystemAdmin only, via `ITenantContext.IsSystemAdmin`. Handlers: `ListExternalIdentitiesQueryHandler:31`, `GetLoginAuditQueryHandler:39`.
  - **Tier 2 — within-tenant ownership relaxation** (see all rows in own tenant vs only own-owned; STILL tenant-bound): Admin-or-SystemAdmin, via `User.IsInRole`. Handler: `ListDelegationsQueryHandler:36`.
- A tenant Admin is non-privileged in Tier 1 because they already see their WHOLE tenant via the `EXISTS([identity].AspNetUsers ... TenantId=@CallerTenantId)` predicate. Adding Admin to Tier 1 would be a **regression** (cross-tenant login/federation data leak).

## Changes (3 files, NO handler logic touched)
1. `HttpContextTenantContext.IsSystemAdmin` — `Roles.SystemAdmin` constant instead of `"SystemAdmin"` string literal (behavior-identical, lockstep with role-grant guards, typo-proof).
2. Documented canonical two-tier privilege convention table in `docs/code-standards.md` (Security Patterns section).
3. Closed `EXTID-LOGINAUDIT-PRIV-CONVENTION` backlog row.

## What didn't / lessons
- **Before "fixing" a flagged inconsistency, verify the operations are the SAME.** A differing role-set across handlers is often **correct** because the threat surfaces differ. Validating the audit finding against the real threat model (cross-tenant escape vs within-tenant relaxation) transformed a "fix the inconsistency" task into a "document why it's correct + hygiene" task. The constant swap + table doc is the real work.

## Decisions made
- Keep Tier 1 as SystemAdmin-only (user confirmed). Cross-tenant login/federation data is more sensitive than delegation visibility; tenant Admin loses nothing (already sees all rows in own tenant via predicate).
- Constant swap is hygiene, not a bug-fix — behavior before/after identical.

## Test status
Build 0W/0E `/warnaserror` · ExternalIdentities unit 28/28 · code-review APPROVE 0-block (doc table verified exact-quote accurate vs handler code; constant swap behavior-preserving; no contradiction with the `X-Tenant-Id` override section).

## Next session
- `EXTID-WRITE-TENANT-SCOPE` (T2 — cross-tenant write-leak, ExternalIdentities Create/Update/Delete, same controller; NOT fixed, read-only scope).
- `AUDIT-IDENTITY-CROSSSCHEMA-DEBT` (T4 — `EXISTS` tech-debt vs module-client trend).
- CI still parked; push timing deferred by user.
