import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /ai/search — AI query catalog and NLQ execution
export const aiSearchRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/ai/search',
  component: withModuleFlag('ai', lazyRouteComponent(() => import('@/features/ai'), 'AiSearchPage')),
});
