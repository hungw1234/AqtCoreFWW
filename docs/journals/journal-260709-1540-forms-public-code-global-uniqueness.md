# PUBLIC-FORMS-CODE-GLOBAL-UNIQUENESS: Cross-Tenant Resolution Race Fixed

**Date**: 2026-07-09 15:40
**Severity**: Medium
**Component**: Forms.Public / RLS
**Status**: Resolved

## What Happened

The anonymous public form resolution (`GetPublicFormQueryHandler`, `SubmitPublicFormCommandHandler`, `GetPublicSignatureDocHashQueryHandler`) uses by-Code lookup with **zero TenantId filter**: `WHERE Code=@Code AND Status='Active' AND IsDeleted=0 AND IsPublic=1`. 

`FormTemplate.Code` carries a **unique index per-tenant only** (`HasIndex(t => new { t.TenantId, t.Code }).IsUnique()`). Result: two tenants each with an Active + IsPublic=1 template sharing a Code → the three handlers use `QueryFirstOrDefault` with **no ORDER BY** → resolution is arbitrary. A citizen submits to the wrong tenant's form; a signature doc is attributed to the wrong tenant. Not a private-data leak (IsPublic=1 only), but latent-reachable after the Active→IsPublic=1 backfill earlier this week.

## The Brutal Truth

The fix itself is straightforward. The trap is the guard-test pattern — and we nearly walked into it.

The app-guard that prevents IsPublic enable on a cross-tenant-colliding Code **must use the Dapper `public_forms_bypass` probe**, NOT an EF `AnyAsync`. The RLS FILTER on `forms.FormTemplates` (`IS_MEMBER('db_owner')=1 OR TenantId=SESSION_CONTEXT('TenantId') OR (IsPublic=1 AND public_forms_bypass='true')`) hides other tenants' rows from a pinned-tenant admin under least-privilege login. An EF `AnyAsync` would see only same-tenant → miss the cross-tenant collision in prod yet **PASS in integration tests** (run as sa/db_owner, RLS bypassed). That's a false-green that ships undetected until customer reports it.

The code-review flag correctly caught the sa-only guard test. We closed it by adding `PublicFormsLeastPrivilegeTests` Test 8 (least-priv login, Dapper bypass probe, with-bypass sees cross-tenant row; without-bypass returns null). This is the same mechanism the public-form resolution uses — proving the bypass is load-bearing and catching the RLS-invisible bug before it ships.

## Technical Details

**Fix (Option A — user-approved):**

1. Filtered unique index on `forms.FormTemplates(Code)` WHERE `IsPublic=1 AND IsDeleted=0` declared in `FormTemplateConfiguration.cs`, migration `20260709082745_AddFormTemplatePublicCodeUniqueIndex.cs`. Migration includes fail-loud pre-flight: `STRING_AGG` of colliding Codes → `THROW 50000` if cross-tenant dupes already in DB (never silently demotes).

2. App-guard in `SetFormTemplatePublicAccessCommandHandler`: enabling IsPublic when Code already public in another tenant → 409 `FormErrors.PublicCodeConflict` (`GOV_FRM_019`). Uses Dapper `QueryFirstOrDefaultWithPublicFormsBypassAsync` (the bypass flag, same as resolution).

3. New error factory in `FormErrors.cs`; doc-only note on `IReadDbConnection.cs`.

**Guard-test:**
- `PublicFormsLeastPrivilegeTests.Test_8_SetPublicAccess_CrossTenantCodeCollision_BypassIsLoadBearing`: least-priv login, Dapper bypass (on/off), asserts with-bypass finds cross-tenant row, without-bypass doesn't. Proves RLS-isolation + bypass mechanism.

**Unchanged:** PATCH endpoint, command, DTO, the three by-Code SQLs, per-tenant `(TenantId, Code)` index.

## Root Cause Analysis

1. **Design assumption flipped:** Code was scoped per-tenant; public-access feature assumed global uniqueness was UX unnecessary. Product gate confirmed: public Codes **should be** globally unique.

2. **RLS opacity:** the least-privilege filter is invisible to db_owner tests. A developer testing with sa-level access sees "no collision, AnyAsync returns false" and ships. Prod runs non-db_owner → RLS hides sibling tenants → same probe silently succeeds → collision materializes at runtime.

## Lessons Learned

**The sa/db_owner false-green trap remains the highest-risk pattern.** RLS-dependent checks in least-priv code **must have a non-sa guard test** using the actual bypass mechanism (same code path as production). Unit/EF tests alone are insufficient; they bypass RLS. This is the third time this week the pattern bit us (twice caught, once not). Checklist for any RLS-gated guard:

- Does the guard use a dedicated RLS-visible probe (Dapper with bypass flag, not EF AnyAsync)?
- Is there a least-priv test proving the probe is load-bearing?
- Does the least-priv test assert BOTH with-bypass and without-bypass outcomes?

The DB filtered unique index is the true enforcement (RLS-independent); the 409 guard is UX polish + TOCTOU-free check→write.

## Next Steps

- Backlog row PUBLIC-FORMS-CODE-GLOBAL-UNIQUENESS marked DONE.
- Monitor for customer reports of cross-tenant code collisions (should be impossible now, DB enforced).
- Lesson [[sa-db-owner-false-greens-rls-assertions]] → update MEMORY feedback file with this instance.

---

**Status**: DONE

**Summary**: Cross-tenant public-form Code collision (arbitrary resolution) fixed via filtered unique index + app-guard (Dapper bypass probe, not EF). Guard-test pattern closed the sa-only false-green trap.

**Concerns**: None.
