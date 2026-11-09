import { Tabs } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSodRules } from './sod-rules-api';
import { SodRulesList } from './sod-rules-list';
import { SodViolationsTab } from './sod-violations-tab';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { usePermissions } from '@/features/auth/use-permissions';

// Permission code for viewing violation audit log (seeded to Admin role in P1)
const VIEW_AUDIT_LOG_PERM = 'IDENTITY.SOD.VIEW_AUDIT_LOG';

/** SoD Rules admin page — tabbed container: Rules (default) + Violations (audit log) */
export function SodRulesPage() {
  const { t } = useTranslation();
  const { data } = useSodRules();
  const { hasPermission } = usePermissions();

  const rules = data ?? [];
  const total = rules.length;
  const canViewAuditLog = hasPermission(VIEW_AUDIT_LOG_PERM);

  const tabItems = [
    {
      key: 'rules',
      label: t('sodRules.tabs.rules', 'Quy tac'),
      children: <SodRulesList />,
    },
    {
      key: 'violations',
      label: t('sodRules.tabs.violations', 'Nhat ky vi pham'),
      children: <SodViolationsTab />,
      disabled: !canViewAuditLog,
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('nav.sodRules', 'Quy tac phan tach nhiem vu (SoD)')}
        stats={{ total, label: t('sodRules.stats.rules', 'quy tac') }}
      />
      <Tabs
        defaultActiveKey="rules"
        items={tabItems}
        style={{ marginTop: 16 }}
      />
    </div>
  );
}
