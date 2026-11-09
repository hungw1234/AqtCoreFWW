import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/data-classification — data classification inventory + rules (QĐ 742 §6.2.a)
// Requires: COMPLIANCE.CLASSIFICATION.VIEW permission
export const adminDataClassificationRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/data-classification',
  component: lazyRouteComponent(
    () => import('@/features/data-classification/classification-page'),
    'ClassificationPage',
  ),
});
