import { createRoute } from '@tanstack/react-router';
import { rootRoute } from './root-layout';
import { CallbackPage } from '@/features/auth';

export const callbackRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/callback',
  component: CallbackPage,
});
