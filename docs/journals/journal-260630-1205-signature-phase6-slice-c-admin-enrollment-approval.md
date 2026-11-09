# Phase 6 Slice C: Admin Enrollment Approval Page

**Date**: 2026-06-30 12:05
**Severity**: Medium
**Component**: Digital Signature / Admin UI
**Status**: Resolved

## What Happened

Shipped standalone `/admin/signature-enrollments` page (gated SYSTEM.CONFIG.MANAGE) for pending signing-cert enrollment queue. Approve/Reject actions + JWT-tenant scope banner + paginated table.

## The Brutal Truth

The plan M4 said "fold approval into existing /admin/signature-config" — sounded clean, wrong assumption. When Phase 3 added the BE SignerCertEnrollmentAdminController guard, it became JWT-tenant-scoped and deliberately ignores X-Tenant-Id override. The config page is a cross-tenant OVERRIDE tool. Mixing both scopes on one screen confuses the UX and violates the security boundary. Scout caught this AFTER the plan was locked; user approved the standalone deviation. The lesson: don't trust a UI-placement decision written 3 phases ago — re-verify the backend tenant scoping BEFORE coding.

## Technical Details

- **Pagination trap**: BE returns nested SharedKernel `PagedResult<T>` ({items, totalCount, meta:{page,...}}), NOT flat `PaginatedResult<T>` from @/shared/types/api. Created separate type; read items+totalCount from top level only.
- **Files**: 5 new component files + types + routes + admin-menu-entries (SECURITY_PATHS).
- **Pattern**: Table + useServerPagination + hand-written signature-api hooks, mirrors user-list-page.
- **Gates**: vitest 4 new → 1229/1229 ✓, tsc clean, eslint clean. code-reviewer APPROVE_WITH_NITS (0 Crit/High, 1 Medium: SECURITY_PATHS missing new route → menu highlight broken, fixed; 2 Low: per-row approvingId loading state + hardcoded vi-VN date locale dropped).

## Lessons Learned

1. **Re-verify tenant scoping**: A security guard added in a dependent phase can invalidate placement decisions from earlier planning. Always grep BE controller for QueryFilter/Guard before UI placement.
2. **API contract drift**: Paginated endpoints don't have uniform return shapes — read docs/test payload before building types.
3. **SECURITY_PATHS is a second gate**: New routes must update both router + menu + SECURITY_PATHS + breadcrumb config, not just router.

## Next Steps

- P6A+C done. P6B (self-service enroll) blocked on missing set-CCCD endpoint. P6D (placement config) pending.
- No push — awaiting finalize gate.

---

Status: DONE | Standalone page shipped, design pivot documented, tenant-scope guard revalidated.
