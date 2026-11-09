import { createRouter } from '@tanstack/react-router';
import { RouteErrorFallback } from '@/shared/components/route-error-fallback';
import { rootRoute } from '@/routes/root-layout';
import { authenticatedRoute } from '@/routes/authenticated-layout';
import { adminRoute } from '@/routes/admin-layout';
import { loginRoute } from '@/routes/login-page';
import { callbackRoute } from '@/routes/callback-page';
import { auditLogsRoute } from '@/routes/audit-logs-page';
import { adminUsersRoute } from '@/routes/admin-users-page';
import { adminTenantsRoute } from '@/routes/admin-tenants-page';
import { adminSystemParamsRoute } from '@/routes/admin-system-params-page';
import { adminMasterDataRoute } from '@/routes/admin-master-data-page';
import { adminOrganizationRoute } from '@/routes/admin-organization-page';
import { adminApiKeysRoute } from '@/routes/admin-api-keys-page';
import { adminHealthRoute } from '@/routes/admin-health-page';
import { adminWebhookDeliveriesRoute } from '@/routes/admin-webhook-deliveries-page';
import { adminAbacRulesRoute } from '@/routes/admin-abac-rules-page';
import { adminDelegationsRoute } from '@/routes/admin-delegations-page';
import { adminAccessReviewsRoute } from '@/routes/admin-access-reviews-page';
import { adminSessionsRoute } from '@/routes/admin-sessions-page';
import { adminBackupRoute } from '@/routes/admin-backup-page';
import { adminWorkflowRoute } from '@/routes/admin-workflow-page';
import { adminWorkflowDetailRoute } from '@/routes/admin-workflow-detail-page';
import { adminWorkflowAssignmentsRoute } from '@/routes/admin-workflow-assignments-page';
import { adminWorkflowInstancesRoute } from '@/routes/admin-workflow-instances-page';
import { adminWorkflowInstanceDetailRoute } from '@/routes/admin-workflow-instance-detail-page';
import { adminWorkflowDelegationRoute } from '@/routes/admin-workflow-delegation-page';
import { adminWorkflowAnalyticsRoute } from '@/routes/admin-workflow-analytics-page';
import { adminWorkflowMigrationDetailRoute } from '@/routes/admin-workflow-migration-detail-page';
import { adminNotificationTemplatesRoute } from '@/routes/admin-notification-templates-page';
import { workflowInboxRoute } from '@/routes/workflow-inbox-page';
import { dashboardRoute } from '@/routes/dashboard-page';
import { reportDefinitionsRoute } from '@/routes/report-definitions-page';
import { reportExecutionsRoute } from '@/routes/report-executions-page';
import { formsRoute } from '@/routes/forms-list-page';
import { formDetailRoute } from '@/routes/form-detail-page';
import { filesRoute } from '@/routes/files-list-page';
import { notificationsRoute } from '@/routes/notifications-list-page';
import { aiSearchRoute } from '@/routes/ai-search-page';
import { profileRoute } from '@/routes/profile-page';
import { rolesRoute } from '@/routes/roles-page';
import { notFoundRoute } from '@/routes/not-found-page';
import { adminRulesRoute } from '@/routes/admin-rules-page';
import { adminRulesDetailRoute } from '@/routes/admin-rules-detail-page';
import { adminTemplatesRoute } from '@/routes/admin-templates-page';
import { adminPendingWorkflowLinksRoute } from '@/routes/admin-pending-workflow-links-page';
import { adminJobsRoute } from '@/routes/admin-jobs-page';
import { adminAiRoute } from '@/routes/admin-ai-page';
import { adminMenusRoute } from '@/routes/admin-menus-page';
import { copilotRoute } from '@/routes/copilot-page';
import { chatRoute } from '@/routes/chat-page';
import { integrationPartnersRoute } from '@/routes/integration-partners-page';
import { integrationContractsRoute } from '@/routes/integration-contracts-page';
import { integrationMessageLogsRoute } from '@/routes/integration-message-logs-page';
import { publicFormRoute } from '@/routes/public-form-page';
import { consentRoute } from '@/routes/consent-page';
import { adminRtbfRoute } from '@/routes/admin-rtbf-page';
import { adminCredentialPoliciesRoute } from '@/routes/admin-credential-policies-page';
import { adminMfaPoliciesRoute } from '@/routes/admin-mfa-policies-page';
import { adminDataClassificationRoute } from '@/routes/admin-data-classification-page';
import { adminExternalIdentitiesRoute } from '@/routes/admin-external-identities-page';
import { adminIpWhitelistRoute } from '@/routes/admin-ip-whitelist-page';
import { adminAuditPolicyRoute } from '@/routes/admin-audit-policy-page';
import { adminBackupPolicyRoute } from '@/routes/admin-backup-policy-page';
import { adminSecurityHealthLoginPolicyRoute } from '@/routes/admin-security-health-login-policy-page';
import { adminSecurityHealthPasswordPolicyRoute } from '@/routes/admin-security-health-password-policy-page';
import { adminSecurityHealthAuditRetentionRoute } from '@/routes/admin-security-health-audit-retention-page';
import { adminSecurityHealthAuditIntegrityRoute } from '@/routes/admin-security-health-audit-integrity-page';
import { adminSecurityHealthKeyRotationRoute } from '@/routes/admin-security-health-key-rotation-page';
import { adminSecurityHealthBackupHistoryRoute } from '@/routes/admin-security-health-backup-history-page';
import { accountMfaEnrolmentRoute } from '@/routes/account-mfa-enrolment-page';
import { adminGroupsRoute } from '@/routes/admin-groups-page';
import { adminDataScopesRoute } from '@/routes/admin-data-scopes-page';
import { adminSodRulesRoute } from '@/routes/admin-sod-rules-page';
import { adminPolicyRulesRoute } from '@/routes/admin-policy-rules-page';
import { adminJitProviderConfigsRoute } from '@/routes/admin-jit-provider-configs-page';
import { adminWorkflowStatesRoute } from '@/routes/admin-workflow-states-page';
import { adminWorkflowActionsRoute } from '@/routes/admin-workflow-actions-page';
import { changePasswordRoute } from '@/routes/change-password-page';
import { adminSignatureConfigRoute } from '@/routes/admin-signature-config-page';
import { adminSignatureEnrollmentsRoute } from '@/routes/admin-signature-enrollments-page';
import { digitalSignatureDemoRoute } from '@/routes/digital-signature-demo-page';
import { businessProcessRunNewRoute } from '@/routes/business-process-run-new-page';
import { businessProcessRunDetailRoute } from '@/routes/business-process-run-detail-page';
import { adminBusinessProcessesRoute } from '@/routes/admin-business-processes-page';
import { adminBusinessProcessNewRoute } from '@/routes/admin-business-process-new-page';
import { adminBusinessProcessDetailRoute } from '@/routes/admin-business-process-detail-page';

// Route tree — public routes + authenticated subtree + admin guard subtree
const routeTree = rootRoute.addChildren([
  loginRoute,
  callbackRoute,
  changePasswordRoute,
  publicFormRoute,
  authenticatedRoute.addChildren([
    dashboardRoute,
    rolesRoute,
    auditLogsRoute,
    workflowInboxRoute,
    reportDefinitionsRoute,
    reportExecutionsRoute,
    formsRoute,
    formDetailRoute,
    filesRoute,
    notificationsRoute,
    aiSearchRoute,
    profileRoute,
    accountMfaEnrolmentRoute,
    copilotRoute,
    chatRoute,
    integrationPartnersRoute,
    integrationContractsRoute,
    integrationMessageLogsRoute,
    consentRoute,
    digitalSignatureDemoRoute,
    businessProcessRunNewRoute,
    businessProcessRunDetailRoute,
    // Admin routes — nested under adminRoute (enforces Admin/SystemAdmin role)
    adminRoute.addChildren([
      adminUsersRoute,
      adminTenantsRoute,
      adminSystemParamsRoute,
      adminMasterDataRoute,
      adminOrganizationRoute,
      adminApiKeysRoute,
      adminWebhookDeliveriesRoute,
      adminBackupRoute,
      adminAbacRulesRoute,
      adminDelegationsRoute,
      adminAccessReviewsRoute,
      adminSessionsRoute,
      adminHealthRoute,
      adminWorkflowRoute,
      adminWorkflowDetailRoute,
      adminWorkflowMigrationDetailRoute,
      adminWorkflowAssignmentsRoute,
      adminWorkflowInstancesRoute,
      adminWorkflowInstanceDetailRoute,
      adminWorkflowDelegationRoute,
      adminWorkflowAnalyticsRoute,
      adminNotificationTemplatesRoute,
      adminRulesRoute,
      adminRulesDetailRoute,
      adminTemplatesRoute,
      adminPendingWorkflowLinksRoute,
      adminJobsRoute,
      adminAiRoute,
      adminMenusRoute,
      adminRtbfRoute,
      adminCredentialPoliciesRoute,
      adminMfaPoliciesRoute,
      adminExternalIdentitiesRoute,
      adminGroupsRoute,
      adminDataScopesRoute,
      adminSodRulesRoute,
      adminPolicyRulesRoute,
      adminJitProviderConfigsRoute,
      adminWorkflowStatesRoute,
      adminWorkflowActionsRoute,
      adminDataClassificationRoute,
      // Phase-05 QĐ 742 — 3 CRUD pages + 6 D7 Security Health pages
      adminIpWhitelistRoute,
      // Phase-05b QĐ 742 — digital-signature admin config
      adminSignatureConfigRoute,
      // Phase-06 Slice C — self-service enrollment approval queue (JWT-tenant, H4)
      adminSignatureEnrollmentsRoute,
      adminAuditPolicyRoute,
      adminBackupPolicyRoute,
      adminSecurityHealthLoginPolicyRoute,
      adminSecurityHealthPasswordPolicyRoute,
      adminSecurityHealthAuditRetentionRoute,
      adminSecurityHealthAuditIntegrityRoute,
      adminSecurityHealthKeyRotationRoute,
      adminSecurityHealthBackupHistoryRoute,
      // Phase-07 — BP Admin UI
      adminBusinessProcessesRoute,
      adminBusinessProcessNewRoute,
      adminBusinessProcessDetailRoute,
    ]),
  ]),
  notFoundRoute,
]);

export const router = createRouter({
  routeTree,
  defaultPreload: 'intent',
  defaultErrorComponent: RouteErrorFallback,
});

// Register router for type safety across the app
declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router;
  }
}
