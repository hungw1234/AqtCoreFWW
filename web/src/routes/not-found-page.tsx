import { createRoute } from '@tanstack/react-router';
import { rootRoute } from './root-layout';
import { NotFoundPage } from '@/shared/components/not-found-page';

// Catch-all route — renders 404 for any unmatched path
export const notFoundRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '*',
  component: NotFoundPage,
});
