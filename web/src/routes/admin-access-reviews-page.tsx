import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/access-reviews — pending access review requests (approve / reject)
export const adminAccessReviewsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/access-reviews',
  component: lazyRouteComponent(
    () => import('@/features/access-reviews/access-review-page'),
    'AccessReviewPage',
  ),
});
