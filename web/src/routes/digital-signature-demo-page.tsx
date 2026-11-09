import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /digital-signature/demo — user-facing signing demo (QĐ742 §5.1)
// Any authenticated user — no admin permission required.
export const digitalSignatureDemoRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/digital-signature/demo',
  component: lazyRouteComponent(
    () => import('@/features/digital-signature/signature-demo-page'),
    'SignatureDemoPage',
  ),
});
