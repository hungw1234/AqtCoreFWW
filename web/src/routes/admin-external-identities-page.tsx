import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/external-identities — SSO/OAuth/LDAP/SAML identity links management
export const adminExternalIdentitiesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/external-identities',
  component: lazyRouteComponent(
    () => import('@/features/external-identities/external-identities-page'),
    'ExternalIdentitiesPage',
  ),
});
