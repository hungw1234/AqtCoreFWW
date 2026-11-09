import { createRoute } from '@tanstack/react-router';
import { lazy, Suspense } from 'react';
import { Spin } from 'antd';
import { adminRoute } from './admin-layout';
import { RouteGuard } from '@/features/auth/route-guard';

// Lazy-load page chunk
const TenantListPage = lazy(() =>
  import('@/features/tenants').then((m) => ({ default: m.TenantListPage })),
);

// Strict per-route gate by permission — replaces role-based AdminGuard for this page.
function GuardedTenantListPage() {
  return (
    <RouteGuard requiredPermissions={['IDENTITY.TENANT.VIEW']}>
      <Suspense fallback={<Spin />}>
        <TenantListPage />
      </Suspense>
    </RouteGuard>
  );
}

// Route: /admin/tenants
export const adminTenantsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/tenants',
  component: GuardedTenantListPage,
});
