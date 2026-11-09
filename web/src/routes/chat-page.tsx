import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /chat — real-time conversation with SignalR (P2-01)
export const chatRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/chat',
  component: withModuleFlag('collaboration', lazyRouteComponent(
    () => import('@/features/collaboration'),
    'ChatPage',
  )),
});
