import { createRoute, Outlet } from '@tanstack/react-router';
import { Spin } from 'antd';
import { authenticatedRoute } from './authenticated-layout';
import { usePermissions } from '@/features/auth';
import { ForbiddenPage } from '@/shared/components/forbidden-page';

// Admin layout route — coarse gate: blocks users with zero admin permissions
// (e.g. Citizen, Viewer) from entering /admin/*. Strict per-route gating is
// enforced by RouteGuard requiredPermissions on each child route.
// Sidebar (BE-filtered via /menus/my-menus) already hides admin links from
// these users, but this guard catches direct URL access.
export const adminRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  id: '_admin',
  component: AdminGuard,
});

function AdminGuard() {
  const { permissions, isPermissionsLoading } = usePermissions();

  // Show spinner while /me/permissions is loading — prevents 403-flash on first
  // mount and after tenant-switch (cache invalidation drops me/permissions while refetching).
  if (isPermissionsLoading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh' }}>
        <Spin size="large" />
      </div>
    );
  }

  // Any user with at least one effective permission may enter the admin shell;
  // page-level RouteGuard then enforces the specific permission per feature.
  if (permissions.length === 0) {
    return <ForbiddenPage />;
  }

  return <Outlet />;
}
