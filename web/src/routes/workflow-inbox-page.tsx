import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /inbox — workflow inbox for current user, requires auth
export const workflowInboxRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/inbox',
  component: lazyRouteComponent(() => import('@/features/inbox'), 'WorkflowInboxPage'),
});
