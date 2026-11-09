import { createRoute } from '@tanstack/react-router';
import { rootRoute } from './root-layout';
import { RouteGuard } from '@/features/auth';
import { AppLayout } from '@/layouts/app-layout';

// authenticatedRoute: layout route that gates all protected child routes
export const authenticatedRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: '_authenticated',
  component: AuthenticatedLayout,
});

function AuthenticatedLayout() {
  return (
    <RouteGuard>
      <AppLayout />
    </RouteGuard>
  );
}
