import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /files — file management with upload dragger
export const filesRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/files',
  component: lazyRouteComponent(() => import('@/features/files'), 'FileListPage'),
});
