import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /integration/message-logs — integration message log list (M12)
export const integrationMessageLogsRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/integration/message-logs',
  component: lazyRouteComponent(
    () => import('@/features/integration'),
    'MessageLogListPage',
  ),
  validateSearch: (search: Record<string, unknown>) => ({
    page: Number(search['page'] ?? 1),
    pageSize: Number(search['pageSize'] ?? 20),
  }),
});
