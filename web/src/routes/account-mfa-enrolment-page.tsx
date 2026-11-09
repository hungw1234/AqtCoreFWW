import { createRoute, lazyRouteComponent } from '@tanstack/react-router';
import { authenticatedRoute } from './authenticated-layout';

// Route: /account/mfa — TOTP enrolment wizard for the current authenticated user.
// Lives under authenticatedRoute (not adminRoute): any logged-in user may enrol.
// Backend endpoints are [Authorize] only for begin/verify; [RequiresMfa] for disable/regen.
export const accountMfaEnrolmentRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/account/mfa',
  component: lazyRouteComponent(
    () => import('@/features/account/mfa-enrolment-page'),
    'MfaEnrolmentPage',
  ),
});
