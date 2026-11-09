import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/signature-config — tenant digital-signature configuration (QĐ742 §5.1)
// Requires: SECURITY.SIGNATURE_CONFIG.VIEW permission
export const adminSignatureConfigRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/signature-config',
  component: lazyRouteComponent(
    () => import('@/features/digital-signature/signature-config-page'),
    'SignatureConfigPage',
  ),
});
