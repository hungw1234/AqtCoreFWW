import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/signature-enrollments — admin review queue for self-service signing-cert enrollments.
// Requires: SIGNATURE.ENROLLMENT.APPROVE. Tenant-scoped to the admin's JWT claim (H4 — not the X-Tenant-Id
// override), so this is a standalone page rather than a tab on the cross-tenant signature-config page.
// validateSearch declares page/pageSize so useServerPagination's URL sync is not stripped by the router.
export const adminSignatureEnrollmentsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/signature-enrollments',
  component: lazyRouteComponent(
    () => import('@/features/digital-signature/enrollments/signature-enrollment-admin-page'),
    'SignatureEnrollmentAdminPage',
  ),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
