# Journal — ExternalIdentities Write-Path Tenant Isolation (Cross-Tenant TAMPERING Fix)

**Date:** 2026-06-11 (3rd session) · **Commit:** `8e72f372` (NOT PUSHED; 3 commits ahead origin)

## Session goal
`/aqt-next` → `/cook EXTID-WRITE-TENANT-SCOPE` — harvest the write-half left open by the morning read-leak fix (`a9dd8a8f`). The 3 write handlers (Create/Update/Delete) on the same `ExternalIdentitiesController` had **zero tenant checks** = account-takeover + enumeration DoS vector on every tenant's linked identities.

## What worked
- **Vector verified empirically** (not assumed): (1) `CreateExternalIdentityCommandHandler` accepts arbitrary `UserId` Guid from request, verifies user-exists but NOT caller-tenant → Admin in tenant-A links attacker SSO/LDAP identity to a user in tenant-B = complete account hijack. (2) Update/Delete use `GetByIdAsync(long id)` with NO predicate = sequential bigint enumerable; any authenticated caller mutates/unlinks any tenant's external links. (3) `IdentityDbContext` applies NO global tenant filter on `ApplicationUser` (verified grep, not assumption); EF does not auto-scope.
- **Fix shipped** (Model C, no migration): threaded `Guid? CallerTenantId + bool IsPrivileged` into all 3 commands from `tenantContext.{TenantId, IsSystemAdmin}` (JWT-derived, controller-set, never from request body). Handlers fail-closed: `IsPrivileged || (CallerTenantId.HasValue && ownerTenantId == CallerTenantId)` else **NotFound** (not Forbidden, mirrors read-side, no existence leak). Update/Delete inject `UserManager` to resolve owning user's tenant; SystemAdmin skips lookup.
- **Privilege convention locked** (per morning affirmation): SystemAdmin-only for external-identity + login-audit (no OrAdmin fallback) — more sensitive than delegation; a tenant Admin should not see other tenants' linked identities or auth attempts.
- **Build + tests shipped**: 0W/0E `/warnaserror` (Application+Presentation+Integration). Identity.Application unit 286/286 (+8 cross-tenant + NULL/NULL-tenant edge cases). 9/9 new integration write tenant-isolation tests (Testcontainers, assert victim-intact on deny). code-review APPROVE (0 logic blocks).

## What didn't / lessons
- **The NULL/NULL-tenant trap** (caught by edge tests, **not by assumption**): C# `(Guid?)null == (Guid?)null` is `true` — so the guard MUST include `CallerTenantId.HasValue` or a tenantless caller would pass a tenantless owner. The SQL read-side blocks this implicitly (`= NULL` is UNKNOWN); the C# write-side MUST reproduce it explicitly. Added NULL/NULL edge test per handler (reviewer suggestion accepted) to lock the `.HasValue` invariant against future refactors.
- **Signature-change blast radius**: grepped whole repo incl. root `tests/` for command constructors before changing — only controller (3 sites) + test files. No default params = any missed caller is a compile error under `/warnaserror`. (Prior morning: caller-grep missed root `tests/` entirely.)
- **Mirror established read-pattern for consistency** rather than invent new shape: thread flags + NotFound-not-Forbidden on same controller (read-side already does this; same guard flavor means "read and write deny are identical").
- **Ran Testcontainers myself for empirical truth.** Morning lesson: tester subagent false-passed when files didn't compile. No subagent delegation on this slice; build+test+code-review self. All 9 integ tests hit the DB (cross-tenant reads/writes prove containment).

## Decisions made
- **Fix now** (not deferred) — mirrors the read-fix pattern shipped 2 hours ago; same controller surface; same privilege convention.
- **No soft-delete filter on `GetByIdAsync`** (latent gap, not new): the tenant guard executes BEFORE the dup-check `GetByUserAndProviderAsync`; the duplication-prevent is unscoped but denied early. Reviewer flagged; deferred to follow-up `AUDIT-IDENTITY-CROSSSCHEMA-DEBT` (T4).

## Test status
Build 0W/0E · Identity.Application unit 286/286 (+8 cross-tenant/edge) · Identity.Application.IntegrationTests 9/9 (Testcontainers, cross-tenant write deny + victim-intact assertions) · code-review APPROVE 0-block.

## Next steps
- Push unpushed 3 commits: `6ac62223` (convention doc) + `f71dfed5` (morning journal) + `8e72f372` (this fix).
- Filed follow-up: `AUDIT-IDENTITY-CROSSSCHEMA-DEBT` (T4) — soft-delete filter on `GetByIdAsync`, cross-schema EXISTS debt (migrate to `IIdentityModuleClient`).
- Feature-dev phase active; CI parked (push ≠ CI-verified).
