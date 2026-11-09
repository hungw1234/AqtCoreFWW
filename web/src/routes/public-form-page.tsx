import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { rootRoute } from './root-layout';

// Route: /public/forms/$code — anonymous public form, outside authenticated layout
export const publicFormRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/public/forms/$code',
  component: lazyRouteComponent(
    () => import('@/features/forms/public-form-page'),
    'PublicFormPage'
  ),
});
