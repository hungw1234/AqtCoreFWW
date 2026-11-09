import { useEffect, useRef } from 'react';
import { useAuth } from './use-auth';
import { usePermissions } from './use-permissions';
import { userManager } from './auth-provider';
import { Spin } from 'antd';
import type { ReactNode } from 'react';

interface RouteGuardProps {
  children: ReactNode;
  requiredRoles?: string[];
  requiredPermissions?: string[];
}

// RouteGuard: auto-redirects to OIDC provider if unauthenticated; shows 403 if missing roles/permissions.
// Shows spinner while /me/permissions is loading to avoid 403-flash on first mount + tenant switch.
export function RouteGuard({ children, requiredRoles, requiredPermissions }: RouteGuardProps) {
  const { isAuthenticated, isLoading } = useAuth();
  const { hasRole, hasPermission, isPermissionsLoading } = usePermissions();
  const redirecting = useRef(false);

  useEffect(() => {
    if (!isLoading && !isAuthenticated && !redirecting.current) {
      redirecting.current = true;
      // Capture the deep-link so the OIDC round-trip returns here, not to '/'.
      // callback-page.tsx already consumes state.returnUrl with a relative-path
      // open-redirect guard; pathname+search always starts with '/' → safe.
      userManager.signinRedirect({
        state: { returnUrl: window.location.pathname + window.location.search },
      });
    }
  }, [isLoading, isAuthenticated]);

  if (isLoading || !isAuthenticated || isPermissionsLoading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh' }}>
        <Spin size="large" />
      </div>
    );
  }

  const missingRole = requiredRoles?.some((r) => !hasRole(r));
  const missingPerm = requiredPermissions?.some((p) => !hasPermission(p));

  if (missingRole || missingPerm) {
    return (
      <div style={{ padding: 48, textAlign: 'center' }}>
        <h2>403 — Không có quyền truy cập</h2>
        <p>Bạn không có quyền xem trang này.</p>
      </div>
    );
  }

  return <>{children}</>;
}
