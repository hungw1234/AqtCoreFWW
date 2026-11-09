import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';

// Route: /admin/jit-provider-configs — JIT SSO provider configuration management
export const adminJitProviderConfigsRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/jit-provider-configs',
  component: lazyRouteComponent(
    () => import('@/features/jit-provider-config/jit-provider-config-page'),
    'JitProviderConfigPage',
  ),
});
