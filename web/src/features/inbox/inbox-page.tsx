// inbox-page.tsx — "Hồ sơ của tôi" 2-tab page hosted on /inbox route.
// Tab 1 "Chờ xử lý": workflow tasks pending action (View 1 — InboxTable).
// Tab 2 "Đã xử lý": BP-Run dossiers the user has already processed (View 2 — ProcessedDossiersList).
// Overlap between tabs is intentional and expected (no deduplication).

import { Tabs, Button } from 'antd';
import { InboxOutlined, PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import { usePermissions } from '@/features/auth/use-permissions';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { InboxTable } from './inbox-table';
import { ProcessedDossiersList } from './processed-dossiers-list';

export function WorkflowInboxPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { hasPermission } = usePermissions();

  // Gate the create action on the same permission the sidebar uses for the BP-run wizard entry —
  // a user who can't start a run shouldn't see a button that dead-ends at a 403 on submit.
  const canCreateBpRun = hasPermission('BUSINESSPROCESS.BP.VIEW');

  const tabItems = [
    {
      key: 'pending',
      label: t('page.myDossiers.tabPending'),
      children: <InboxTable />,
    },
    {
      key: 'processed',
      label: t('page.myDossiers.tabProcessed'),
      children: <ProcessedDossiersList />,
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('page.myDossiers.title')}
        icon={<InboxOutlined />}
        actions={
          canCreateBpRun ? (
            <Button
              type="primary"
              icon={<PlusOutlined />}
              onClick={() => navigate({ to: '/business-process-runs/new' })}
            >
              {t('page.myDossiers.newDossier')}
            </Button>
          ) : undefined
        }
      />
      <AdminContentCard noPadding>
        <Tabs
          defaultActiveKey="pending"
          items={tabItems}
          style={{ padding: '0 16px 16px' }}
          destroyOnHidden={false}
        />
      </AdminContentCard>
    </div>
  );
}
