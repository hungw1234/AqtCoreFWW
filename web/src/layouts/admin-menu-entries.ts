import {
  TeamOutlined,
  SafetyCertificateOutlined,
  SettingOutlined,
  // AuditOutlined, // tạm ẩn cùng menu Access Reviews — uncomment khi bật lại /admin/access-reviews
  DatabaseOutlined,
  ApartmentOutlined,
  KeyOutlined,
  BellOutlined,
  HeartOutlined,
  ApiOutlined,
  CloudUploadOutlined,
  SwapOutlined,
  DesktopOutlined,
  NodeIndexOutlined,
  EditOutlined,
  FileProtectOutlined,
  ScheduleOutlined,
  ExperimentOutlined,
  MenuOutlined,
  UsergroupAddOutlined,
  LinkOutlined,
  EyeOutlined,
  // BlockOutlined, // tạm ẩn cùng menu SoD Rules — uncomment khi bật lại /admin/sod-rules
  // StopOutlined, // tạm ẩn cùng menu Policy Rules — uncomment khi bật lại /admin/policy-rules
  // DeleteOutlined, // tạm ẩn cùng menu RTBF — uncomment khi bật lại /admin/rtbf
  LockOutlined,
  BarChartOutlined,
  GlobalOutlined,
  AuditOutlined,
  SafetyOutlined,
  MedicineBoxOutlined,
  SignatureOutlined,
  DashboardOutlined,
} from '@ant-design/icons';
import React from 'react';
import { WorkflowPermissions } from '@/features/workflow/workflow-permissions';

// Flat menu item (leaf node)
export interface MenuItem {
  key: string;
  label: string;
  icon: React.ReactNode;
  requiredPermission?: string;
  /** Gate this item on a specific role (e.g. 'SystemAdmin'). Independent of requiredPermission. */
  requiredRole?: string;
  badge?: string | number; // e.g. "New", 3
  description?: string; // i18n key for tooltip description
}

// Stable key for the external Hangfire-dashboard link. Not a router route — SidebarMenu intercepts
// this key in its onClick and opens the AuthServer-hosted dashboard in a new tab instead of navigating.
export const HANGFIRE_MENU_KEY = 'external:hangfire';

// Group menu item (sub-menu) — supports recursive nesting (MenuItem | MenuGroup | MenuItemGroup)
export interface MenuGroup {
  key: string;
  label: string;
  icon: React.ReactNode;
  requiredPermission?: string;
  children: (MenuItem | MenuGroup | MenuItemGroup)[];
  badge?: string | number;
}

export type MenuEntry = MenuItem | MenuGroup;

// Paths belonging to Identity & Access category (includes Data Scopes)
export const IDENTITY_PATHS = [
  '/admin/users',
  '/roles',
  '/admin/groups',
  '/admin/external-identities',
  '/admin/jit-provider-configs',
  '/admin/abac-rules',
  '/admin/sod-rules',
  '/admin/policy-rules',
  '/admin/credential-policies',
  '/admin/mfa-policies',
  '/admin/access-reviews',
  '/admin/delegations',
  '/admin/sessions',
  '/admin/data-scopes',
];

// Paths belonging to Content & Workflow category
export const CONTENT_PATHS = [
  '/admin/workflow',
  '/admin/workflow-delegation',
  '/admin/workflow-analytics',
  '/admin/rules',
  '/admin/templates',
  '/admin/pending-workflow-links',
  '/admin/notification-templates',
  '/admin/menus',
  '/admin/business-processes',
];

// Paths belonging to System category
export const SYSTEM_PATHS = [
  '/admin/tenants',
  '/admin/system-params',
  '/admin/master-data',
  '/admin/organization',
  '/admin/jobs',
  '/admin/health',
  '/admin/backup',
  '/admin/rtbf',
  '/admin/data-classification',
];

// Paths belonging to API & Webhooks category (formerly Integration)
export const API_WEBHOOKS_PATHS = [
  '/admin/api-keys',
  '/admin/webhooks',
];

// Paths belonging to AI & Automation category
export const AI_AUTOMATION_PATHS = [
  '/admin/ai',
];

// Paths belonging to Security & Compliance category (phase-05 QĐ 742 + phase-05b)
export const SECURITY_PATHS = [
  '/admin/ip-whitelist',
  '/admin/audit-policy',
  '/admin/backup-policy',
  '/admin/security-health/login-policy',
  '/admin/security-health/password-policy',
  '/admin/security-health/audit-retention',
  '/admin/security-health/audit-integrity',
  '/admin/security-health/key-rotation',
  '/admin/security-health/backup-history',
  '/admin/signature-config',
  '/admin/signature-enrollments',
];

// Admin menu item type for Ant Menu ItemGroup (visual section header) — children can nest submenus (MenuEntry)
export interface MenuItemGroup {
  key: string;
  label: string;
  type: 'group';
  children: MenuEntry[];
}

// Children factories for each flat top-level admin submenu (Option C — 6 slots flattened from old Admin wrapper)

export function getIdentityMenuChildren(t: (key: string) => string): MenuItem[] {
  return [
    { key: '/admin/users', label: t('nav.users'), icon: React.createElement(TeamOutlined), description: 'nav.usersDesc' },
    { key: '/roles', label: t('nav.roles'), icon: React.createElement(SafetyCertificateOutlined), requiredPermission: 'IDENTITY.ROLE.VIEW' },
    { key: '/admin/groups', label: t('nav.groups'), icon: React.createElement(UsergroupAddOutlined) },
    { key: '/admin/external-identities', label: t('nav.externalIdentities'), icon: React.createElement(LinkOutlined) },
    { key: '/admin/jit-provider-configs', label: t('nav.jitProviderConfigs'), icon: React.createElement(ApiOutlined) },
    { key: '/admin/abac-rules', label: t('nav.abacRules'), icon: React.createElement(SafetyCertificateOutlined) },
    // { key: '/admin/sod-rules', ... }, // tạm ẩn
    // { key: '/admin/policy-rules', ... }, // tạm ẩn
    { key: '/admin/credential-policies', label: t('nav.credentialPolicies'), icon: React.createElement(LockOutlined) },
    { key: '/admin/mfa-policies', label: t('nav.mfaPolicies'), icon: React.createElement(SafetyCertificateOutlined), requiredPermission: 'SECURITY.MFA_POLICY.VIEW' },
    // { key: '/admin/access-reviews', ... }, // tạm ẩn
    { key: '/admin/delegations', label: t('nav.delegations'), icon: React.createElement(SwapOutlined) },
    { key: '/admin/sessions', label: t('nav.sessions'), icon: React.createElement(DesktopOutlined) },
    { key: '/admin/data-scopes', label: t('nav.dataScopes'), icon: React.createElement(EyeOutlined), description: 'nav.dataScopesDesc' },
  ];
}

export function getContentMenuChildren(t: (key: string) => string): MenuItem[] {
  return [
    { key: '/admin/business-processes', label: t('nav.businessProcessAdmin'), icon: React.createElement(ApartmentOutlined), requiredPermission: 'BUSINESSPROCESS.BP.VIEW' },
    { key: '/admin/workflow', label: t('nav.workflow'), icon: React.createElement(NodeIndexOutlined) },
    { key: '/admin/workflow-delegation', label: t('nav.workflowDelegation'), icon: React.createElement(SwapOutlined), requiredPermission: 'WORKFLOW.DELEGATION.MANAGE' },
    { key: '/admin/workflow-analytics', label: t('nav.workflowAnalytics'), icon: React.createElement(BarChartOutlined), requiredPermission: WorkflowPermissions.AnalyticsView },
    { key: '/admin/rules', label: t('nav.rules'), icon: React.createElement(EditOutlined) },
    { key: '/admin/templates', label: t('nav.templates'), icon: React.createElement(FileProtectOutlined) },
    { key: '/admin/pending-workflow-links', label: t('nav.pendingWorkflowLinks'), icon: React.createElement(LinkOutlined), requiredPermission: 'FORMS.WFLINK.MANAGE' },
    { key: '/admin/notification-templates', label: t('nav.notificationTemplates'), icon: React.createElement(BellOutlined) },
    { key: '/admin/menus', label: t('nav.menus'), icon: React.createElement(MenuOutlined) },
  ];
}

export function getSystemMenuChildren(t: (key: string) => string): MenuItem[] {
  return [
    { key: '/admin/tenants', label: 'Quản lý tenant', icon: React.createElement(GlobalOutlined), requiredPermission: 'IDENTITY.TENANT.VIEW' },
    { key: '/admin/system-params', label: t('nav.systemParams'), icon: React.createElement(SettingOutlined) },
    { key: '/admin/master-data', label: t('nav.masterData'), icon: React.createElement(DatabaseOutlined) },
    { key: '/admin/organization', label: t('nav.organization'), icon: React.createElement(ApartmentOutlined) },
    { key: '/admin/jobs', label: t('nav.jobs'), icon: React.createElement(ScheduleOutlined) },
    // External link → AuthServer-hosted Hangfire dashboard (new tab). SystemAdmin-only; the AuthServer
    // cookie filter is the real gate (this hides it for UX). Sits next to the recurring-jobs admin page.
    { key: HANGFIRE_MENU_KEY, label: t('nav.hangfire'), icon: React.createElement(DashboardOutlined), requiredRole: 'SystemAdmin', description: 'nav.hangfireDesc' },
    { key: '/admin/health', label: t('nav.healthCheck'), icon: React.createElement(HeartOutlined) },
    { key: '/admin/backup', label: t('nav.backup'), icon: React.createElement(CloudUploadOutlined) },
    // { key: '/admin/rtbf', ... }, // tạm ẩn
    { key: '/admin/data-classification', label: t('nav.dataClassification'), icon: React.createElement(AuditOutlined), requiredPermission: 'COMPLIANCE.CLASSIFICATION.VIEW' },
  ];
}

export function getApiWebhooksMenuChildren(t: (key: string) => string): MenuItem[] {
  return [
    { key: '/admin/api-keys', label: t('nav.apiKeys'), icon: React.createElement(KeyOutlined), description: 'nav.apiKeysDesc' },
    { key: '/admin/webhooks', label: t('nav.webhooks'), icon: React.createElement(ApiOutlined), description: 'nav.webhooksDesc' },
  ];
}

export function getAiAutomationMenuChildren(t: (key: string) => string): MenuItem[] {
  return [
    { key: '/admin/ai', label: t('nav.aiAdmin'), icon: React.createElement(ExperimentOutlined), description: 'nav.aiAdminDesc' },
  ];
}

// Phase-05 QĐ 742 — Security & Compliance category children
// Top 3: CRUD admin pages; bottom group: D7 read-only health snapshot pages.
export function getSecurityMenuChildren(t: (key: string) => string): (MenuItem | MenuGroup)[] {
  return [
    {
      key: '/admin/ip-whitelist',
      label: t('nav.ipWhitelist'),
      icon: React.createElement(SafetyOutlined),
      requiredPermission: 'SECURITY.IP_RULE.VIEW',
    },
    {
      key: '/admin/audit-policy',
      label: t('nav.auditPolicy'),
      icon: React.createElement(AuditOutlined),
      requiredPermission: 'AUDIT.RETENTION.VIEW',
    },
    {
      key: '/admin/backup-policy',
      label: t('nav.backupPolicy'),
      icon: React.createElement(CloudUploadOutlined),
      requiredPermission: 'SYSTEM.CONFIG.MANAGE',
    },
    // Phase-05b QĐ 742 — digital-signature admin config
    {
      key: '/admin/signature-config',
      label: t('nav.signatureConfig'),
      icon: React.createElement(SignatureOutlined),
      requiredPermission: 'SECURITY.SIGNATURE_CONFIG.VIEW',
    },
    // Phase-06 Slice C — self-service signing-cert enrollment approval queue (JWT-tenant, H4).
    // Gated by the dedicated SIGNATURE.ENROLLMENT.APPROVE (least-privilege) — matches the controller policy.
    {
      key: '/admin/signature-enrollments',
      label: t('nav.signatureEnrollments'),
      icon: React.createElement(SafetyCertificateOutlined),
      requiredPermission: 'SIGNATURE.ENROLLMENT.APPROVE',
    },
    // Security Health submenu — 6 D7 read-only pages
    {
      key: 'admin-security-health',
      label: t('nav.securityHealth'),
      icon: React.createElement(MedicineBoxOutlined),
      requiredPermission: 'SECURITY.HEALTH.VIEW',
      children: [
        { key: '/admin/security-health/login-policy', label: t('nav.securityHealthLoginPolicy'), icon: React.createElement(LockOutlined) },
        { key: '/admin/security-health/password-policy', label: t('nav.securityHealthPasswordPolicy'), icon: React.createElement(SafetyCertificateOutlined) },
        { key: '/admin/security-health/audit-retention', label: t('nav.securityHealthAuditRetention'), icon: React.createElement(AuditOutlined) },
        { key: '/admin/security-health/audit-integrity', label: t('nav.securityHealthAuditIntegrity'), icon: React.createElement(SafetyOutlined) },
        { key: '/admin/security-health/key-rotation', label: t('nav.securityHealthKeyRotation'), icon: React.createElement(KeyOutlined) },
        { key: '/admin/security-health/backup-history', label: t('nav.securityHealthBackupHistory'), icon: React.createElement(CloudUploadOutlined) },
      ],
    },
  ];
}

// Determine which admin category the pathname belongs to (used for breadcrumbs + defaultOpenKeys)
export function getAdminCategoryKey(pathname: string): string | null {
  if (IDENTITY_PATHS.some((p) => pathname.startsWith(p))) return 'admin-identity';
  if (CONTENT_PATHS.some((p) => pathname.startsWith(p))) return 'admin-content';
  if (SYSTEM_PATHS.some((p) => pathname.startsWith(p))) return 'admin-system';
  if (API_WEBHOOKS_PATHS.some((p) => pathname.startsWith(p))) return 'admin-api-webhooks';
  if (AI_AUTOMATION_PATHS.some((p) => pathname.startsWith(p))) return 'admin-ai-automation';
  if (SECURITY_PATHS.some((p) => pathname.startsWith(p))) return 'admin-security';
  return null;
}

// Map each admin route to its category i18n key for breadcrumb
export const ADMIN_ROUTE_CATEGORY: Record<string, string> = {
  ...Object.fromEntries(IDENTITY_PATHS.map((p) => [p, 'nav.adminIdentity'])),
  ...Object.fromEntries(CONTENT_PATHS.map((p) => [p, 'nav.adminContent'])),
  ...Object.fromEntries(SYSTEM_PATHS.map((p) => [p, 'nav.adminSystem'])),
  ...Object.fromEntries(API_WEBHOOKS_PATHS.map((p) => [p, 'nav.adminApiWebhooks'])),
  ...Object.fromEntries(AI_AUTOMATION_PATHS.map((p) => [p, 'nav.adminAiAutomation'])),
  ...Object.fromEntries(SECURITY_PATHS.map((p) => [p, 'nav.adminSecurity'])),
};
