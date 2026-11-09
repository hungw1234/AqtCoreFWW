import React from 'react';
import { Badge, Menu, Tooltip } from 'antd';
import type { ItemType, MenuItemType } from 'antd/es/menu/interface';
import {
  DashboardOutlined,
  SettingOutlined,
  AuditOutlined,
  ApiOutlined,
  FileTextOutlined,
  InboxOutlined,
  FormOutlined,
  FolderOutlined,
  RobotOutlined,
  ThunderboltOutlined,
  MessageOutlined,
  // SolutionOutlined, // tạm ẩn cùng menu /consent — uncomment khi bật lại
  TeamOutlined,
  NodeIndexOutlined,
  KeyOutlined,
  ExperimentOutlined,
  HistoryOutlined,
  BarChartOutlined,
  SafetyOutlined,
  ApartmentOutlined,
} from '@ant-design/icons';
import { useNavigate, useLocation } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useThemeMode } from '@/core/theme/use-theme-mode';
import { useModuleFlags } from '@/features/auth/module-flags-api';
import { filterItemsByModuleFlags } from './module-flag-menu-filter';
import { resolveIcon } from './menu-icon-map';
import type { MenuNodeDto } from '@/features/menus';
import {
  type MenuEntry,
  type MenuItem,
  type MenuGroup,
  type MenuItemGroup,
  IDENTITY_PATHS,
  CONTENT_PATHS,
  SYSTEM_PATHS,
  API_WEBHOOKS_PATHS,
  AI_AUTOMATION_PATHS,
  SECURITY_PATHS,
  getIdentityMenuChildren,
  getContentMenuChildren,
  getSystemMenuChildren,
  getApiWebhooksMenuChildren,
  getAiAutomationMenuChildren,
  getSecurityMenuChildren,
  HANGFIRE_MENU_KEY,
} from './admin-menu-entries';
import { oidcConfig } from '@/features/auth/auth-config';

// ─── Client-side badges (notification counts) ────────────────────────────────
const BADGE_MAP: Record<string, string | number> = {
  notifications: 3,
};

// ─── Hardcoded fallback menu — Option C flat structure (2026-04-24) ──────────
// 3 top-level visual group headers + 6 flat admin submenus + 1 admin overview leaf.

type TopLevelEntry = MenuEntry | MenuItemGroup;

function getMenuEntries(t: (key: string) => string): TopLevelEntry[] {
  return [
    // ── Group 1: Nghiệp vụ ──
    {
      key: 'group-business',
      label: t('nav.groupBusiness'),
      type: 'group' as const,
      children: [
        { key: '/', label: t('nav.dashboard'), icon: <DashboardOutlined /> },
        { key: '/inbox', label: t('nav.inbox'), icon: <InboxOutlined /> },
        { key: '/forms', label: t('nav.forms'), icon: <FormOutlined /> },
        { key: '/business-process-runs/new', label: t('nav.businessProcessRuns'), icon: <ApartmentOutlined />, requiredPermission: 'BUSINESSPROCESS.BP.VIEW' },
        { key: '/files', label: t('nav.files'), icon: <FolderOutlined /> },
      ],
    },
    // ── Group 2: AI & Trợ lý ──
    {
      key: 'group-ai-assistant',
      label: t('nav.groupAiAssistant'),
      type: 'group' as const,
      children: [
        { key: '/ai/search', label: t('nav.aiSearch'), icon: <RobotOutlined /> },
        { key: '/copilot', label: t('nav.copilot'), icon: <ThunderboltOutlined /> },
        { key: '/chat', label: t('nav.chat'), icon: <MessageOutlined /> },
      ],
    },
    // ── Group 3: Tích hợp & Báo cáo ──
    {
      key: 'group-integration-reports',
      label: t('nav.groupIntegrationReports'),
      type: 'group' as const,
      children: [
        {
          key: 'integration',
          label: t('nav.integration'),
          icon: <ApiOutlined />,
          children: [
            { key: '/integration/partners', label: t('nav.integrationPartners'), icon: <TeamOutlined /> },
            { key: '/integration/contracts', label: t('nav.integrationContracts'), icon: <FileTextOutlined /> },
            { key: '/integration/message-logs', label: t('nav.integrationMessageLogs'), icon: <MessageOutlined /> },
          ],
        },
        { key: '/audit/logs', label: t('nav.audit'), icon: <AuditOutlined />, requiredPermission: 'AUDIT.LOG.VIEW' },
        {
          key: 'reporting',
          label: t('nav.reporting'),
          icon: <BarChartOutlined />,
          requiredPermission: 'REPORT.DASH.VIEW',
          children: [
            { key: '/reports/definitions', label: t('nav.reportDefinitions'), icon: <FileTextOutlined /> },
            { key: '/reports/executions', label: t('nav.reportExecutions'), icon: <HistoryOutlined /> },
          ],
        },
      ],
    },
    // ── Flat admin submenus (Option C — replaces old monolithic Admin wrapper) ──
    {
      key: 'admin-identity',
      label: t('nav.adminIdentity'),
      icon: <TeamOutlined />,
      requiredPermission: 'IDENTITY.USER.VIEW',
      children: getIdentityMenuChildren(t),
    },
    {
      key: 'admin-content',
      label: t('nav.adminContent'),
      icon: <NodeIndexOutlined />,
      requiredPermission: 'IDENTITY.USER.VIEW',
      children: getContentMenuChildren(t),
    },
    {
      key: 'admin-system',
      label: t('nav.adminSystem'),
      icon: <SettingOutlined />,
      requiredPermission: 'IDENTITY.USER.VIEW',
      children: getSystemMenuChildren(t),
    },
    {
      key: 'admin-api-webhooks',
      label: t('nav.adminApiWebhooks'),
      icon: <KeyOutlined />,
      requiredPermission: 'IDENTITY.USER.VIEW',
      children: getApiWebhooksMenuChildren(t),
    },
    {
      key: 'admin-ai-automation',
      label: t('nav.adminAiAutomation'),
      icon: <ExperimentOutlined />,
      requiredPermission: 'IDENTITY.USER.VIEW',
      children: getAiAutomationMenuChildren(t),
    },
    {
      // No parent-level permission gate: each child carries its own requiredPermission, and the
      // category auto-hides when none are visible (buildFallbackItems drops empty groups). Gating the
      // parent on one child's permission (was SECURITY.IP_RULE.VIEW) wrongly hid the whole category —
      // incl. signature-config / enrollment-approval — from admins holding only those child permissions.
      key: 'admin-security',
      label: t('nav.adminSecurity'),
      icon: <SafetyOutlined />,
      children: getSecurityMenuChildren(t),
    },
    // { key: '/consent', label: t('nav.consent'), icon: <SolutionOutlined /> }, // tạm ẩn — route vẫn sống
  ];
}

// ─── Permission check for fallback mode ──────────────────────────────────────

function hasAccess(perm: string, permissions: string[], roles: string[]): boolean {
  if (permissions.includes(perm)) return true;
  // Role-based fallback — covers case when permissions not loaded yet or role grants blanket access
  if ((perm === 'IDENTITY.USER.VIEW' || perm.startsWith('admin.'))
    && (roles.includes('Admin') || roles.includes('SystemAdmin'))) return true;
  if ((perm === 'AUDIT.LOG.VIEW' || perm === 'IDENTITY.ROLE.VIEW' || perm === 'REPORT.DASH.VIEW')
    && (roles.includes('GovOfficer') || roles.includes('Admin') || roles.includes('SystemAdmin'))) return true;
  return false;
}

// ─── Badge wrapper ───────────────────────────────────────────────────────────

function badgeLabel(label: string, badge?: string | number): React.ReactNode {
  if (badge == null) return label;
  return (
    <>
      {label}
      <Badge
        count={badge}
        size="small"
        style={{
          backgroundColor: typeof badge === 'string' ? '#28A745' : '#007BFF',
          fontSize: 10,
          fontWeight: 600,
          marginLeft: 8,
        }}
      />
    </>
  );
}

// ─── Tooltip wrapper for menu items with description ─────────────────────────

function tooltipLabel(
  label: React.ReactNode,
  description: string | undefined,
  t: (key: string) => string,
): React.ReactNode {
  if (!description) return label;
  // If description starts with 'nav.' it's an i18n key, otherwise plain text from BE
  const tooltipText = description.startsWith('nav.') ? t(description) : description;
  return (
    <Tooltip
      title={tooltipText}
      placement="right"
      mouseEnterDelay={0.5}
    >
      <span>{label}</span>
    </Tooltip>
  );
}

// ─── Build items from API MenuNodeDto[] ──────────────────────────────────────

function buildItemsFromApi(nodes: MenuNodeDto[], t: (key: string) => string): ItemType<MenuItemType>[] {
  return nodes.map((node): ItemType<MenuItemType> => {
    const icon = resolveIcon(node.icon);
    const badge = BADGE_MAP[node.code];
    const rawLabel = badgeLabel(node.title, badge);
    const label = tooltipLabel(rawLabel, node.description ?? undefined, t);

    // Group node: has children but no route → render as visual group header
    if (node.children.length > 0 && !node.route && !node.icon) {
      return {
        key: node.code,
        label: node.title,
        type: 'group' as const,
        children: buildItemsFromApi(node.children, t),
      };
    }

    // Submenu node: has children and has route or icon
    if (node.children.length > 0) {
      return {
        key: node.route ?? node.code,
        label,
        icon,
        children: buildItemsFromApi(node.children, t),
      };
    }

    // Leaf node
    return {
      key: node.route ?? node.code,
      label,
      icon,
    };
  });
}

// ─── Build items from hardcoded fallback ─────────────────────────────────────

function buildFallbackItems(
  entries: readonly TopLevelEntry[],
  permissions: string[],
  roles: string[],
  t: (key: string) => string,
): ItemType<MenuItemType>[] {
  return entries
    .filter((e) => {
      // Permission gate (covers most admin entries).
      if ('requiredPermission' in e && e.requiredPermission && !hasAccess(e.requiredPermission, permissions, roles)) return false;
      // Role gate (e.g. the SystemAdmin-only Hangfire external link). Independent of permission.
      if ('requiredRole' in e && e.requiredRole && !roles.includes(e.requiredRole)) return false;
      return true;
    })
    .flatMap((e): ItemType<MenuItemType>[] => {
      if ('type' in e && e.type === 'group') {
        const groupChildren = buildFallbackItems(e.children, permissions, roles, t);
        if (groupChildren.length === 0) return [];
        return [{ key: e.key, label: e.label, type: 'group' as const, children: groupChildren }];
      }
      if (!('children' in e)) {
        const item = e as MenuItem;
        const label = tooltipLabel(badgeLabel(item.label, item.badge), item.description, t);
        return [{ key: item.key, label, icon: item.icon }];
      }
      const group = e as MenuGroup;
      const visibleChildren = buildFallbackItems(group.children as TopLevelEntry[], permissions, roles, t);
      if (visibleChildren.length === 0) return [];
      return [{ key: group.key, label: badgeLabel(group.label, group.badge), icon: group.icon, children: visibleChildren }];
    });
}

// ─── SidebarMenu component ───────────────────────────────────────────────────

interface SidebarMenuProps {
  permissions: string[];
  roles?: string[];
  /** API-driven menu tree — if provided and non-empty, replaces hardcoded menu */
  menuTree?: MenuNodeDto[];
  onNavigate?: () => void;
  popupSubMenus?: boolean;
  collapsed?: boolean;
}

export function SidebarMenu({
  permissions,
  roles = [],
  menuTree,
  onNavigate,
  popupSubMenus = false,
  collapsed = false,
}: SidebarMenuProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const { isDark } = useThemeMode();
  const { flags } = useModuleFlags();

  // Use API menu if available, otherwise fallback to hardcoded. Either way, strip items
  // routing into deferred modules that are OFF in this deployment (fail-open on flag
  // fetch errors — see module-flags-api; the API 404 is the real gate).
  const items = filterItemsByModuleFlags(
    menuTree && menuTree.length > 0
      ? buildItemsFromApi(menuTree, t)
      : buildFallbackItems(getMenuEntries(t), permissions, roles, t),
    flags,
  );

  // The Hangfire dashboard link is an external tool, not a DB menu node — so the server-driven menu
  // tree (the active path in dev/prod once menus are seeded) doesn't carry it. Append it for SystemAdmin
  // on the API path so it renders there too. On the fallback path it already comes from
  // getSystemMenuChildren (role-gated), so don't double-add.
  if (menuTree && menuTree.length > 0 && roles.includes('SystemAdmin')) {
    items.push({ key: HANGFIRE_MENU_KEY, label: t('nav.hangfire'), icon: <DashboardOutlined /> });
  }

  // Open the correct submenu based on current pathname (Option C — admin groups are top-level submenus)
  const defaultOpenKeys: string[] = (() => {
    const p = location.pathname;
    if (p.startsWith('/integration')) return ['integration'];
    if (p.startsWith('/reports')) return ['reporting'];
    if (IDENTITY_PATHS.some((pref) => p.startsWith(pref))) return ['admin-identity'];
    if (CONTENT_PATHS.some((pref) => p.startsWith(pref))) return ['admin-content'];
    if (SYSTEM_PATHS.some((pref) => p.startsWith(pref))) return ['admin-system'];
    if (API_WEBHOOKS_PATHS.some((pref) => p.startsWith(pref))) return ['admin-api-webhooks'];
    if (AI_AUTOMATION_PATHS.some((pref) => p.startsWith(pref))) return ['admin-ai-automation'];
    // Security & Compliance — open parent + inner health submenu if on D7 page
    if (p.startsWith('/admin/security-health')) return ['admin-security', 'admin-security-health'];
    if (SECURITY_PATHS.some((pref) => p.startsWith(pref))) return ['admin-security'];
    return [];
  })();

  const selectedKey =
    location.pathname.startsWith('/forms/') ? '/forms' :
    location.pathname.startsWith('/ai/') ? '/ai/search' :
    location.pathname.startsWith('/admin/rules/') ? '/admin/rules' :
    location.pathname.startsWith('/admin/integration/partners') ? '/admin/integration/partners' :
    location.pathname.startsWith('/integration/partners') ? '/integration/partners' :
    location.pathname.startsWith('/integration/contracts') ? '/integration/contracts' :
    location.pathname.startsWith('/integration/message-logs') ? '/integration/message-logs' :
    location.pathname.startsWith('/business-process-runs') ? '/business-process-runs/new' :
    location.pathname;

  return (
    <nav role="navigation" aria-label="Main navigation">
      <div className="gov-sidebar-scroll" style={{ flex: 1, overflowY: 'auto', overflowX: 'hidden' }}>
        <Menu
          theme={isDark ? 'dark' : 'light'}
          mode={popupSubMenus ? 'vertical' : 'inline'}
          inlineCollapsed={collapsed}
          selectedKeys={[selectedKey]}
          defaultOpenKeys={collapsed ? [] : defaultOpenKeys}
          items={items}
          onClick={({ key }) => {
            // External link (Hangfire dashboard on the AuthServer) — open a new tab, don't router-navigate.
            // Same-eTLD+1 deploy required: the Strict Identity cookie only rides a same-site nav (see Phase 3).
            if (key === HANGFIRE_MENU_KEY) {
              window.open(`${oidcConfig.authority}/hangfire`, '_blank', 'noopener,noreferrer');
              onNavigate?.();
              return;
            }
            navigate({ to: key });
            onNavigate?.();
          }}
          style={{ border: 'none' }}
        />
      </div>
    </nav>
  );
}
