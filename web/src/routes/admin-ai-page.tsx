import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /admin/ai — AI model profiles and prompt templates (P2-03)
export const adminAiRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/ai',
  component: withModuleFlag('ai', lazyRouteComponent(
    () => import('@/features/ai-admin'),
    'AiAdminPage',
  )),
});
