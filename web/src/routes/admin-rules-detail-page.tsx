import { createRoute } from '@tanstack/react-router';
import { adminRoute } from './admin-layout';
import { RuleSetDetailPage } from '@/features/rules';
import { withModuleFlag } from '@/features/auth/module-flag-gate';

// Route: /admin/rules/$ruleSetId — rule set detail with tabs (P1-06)
export const adminRulesDetailRoute = createRoute({
  getParentRoute: () => adminRoute,
  path: '/admin/rules/$ruleSetId',
  component: withModuleFlag('rules', function RulesDetailRouteComponent() {
    const { ruleSetId } = adminRulesDetailRoute.useParams();
    return <RuleSetDetailPage ruleSetId={ruleSetId} />;
  }),
});
