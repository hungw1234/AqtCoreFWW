import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /admin/rules — rule set list (P1-06)
export const adminRulesRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/rules',
  component: withModuleFlag('rules', lazyRouteComponent(
    () => import('@/features/rules'),
    'RuleSetListPage',
  )),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
