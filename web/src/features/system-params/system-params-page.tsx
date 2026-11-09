import { Tabs } from 'antd';
import { SettingOutlined, FlagOutlined, NotificationOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { ParamsTable } from './params-table';
import { FeatureFlagsTab } from './feature-flags-tab';
import { AnnouncementsTab } from './announcements-tab';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

// SystemParamsPage — tabbed: system params | feature flags | announcements
export function SystemParamsPage() {
  const { t } = useTranslation();

  const TAB_ITEMS = [
    {
      key: 'params',
      label: <span><SettingOutlined /> {t('page.admin.systemParams.tab.params')}</span>,
      children: <ParamsTable />,
    },
    {
      key: 'flags',
      label: <span><FlagOutlined /> {t('page.admin.systemParams.tab.flags')}</span>,
      children: <FeatureFlagsTab />,
    },
    {
      key: 'announcements',
      label: <span><NotificationOutlined /> {t('page.admin.systemParams.tab.announcements')}</span>,
      children: <AnnouncementsTab />,
    },
  ];

  return (
    <div>
      <AdminPageHeader title={t('page.admin.systemParams.title')} />
      <AdminContentCard>
        <Tabs defaultActiveKey="params" items={TAB_ITEMS} destroyOnHidden={false} />
      </AdminContentCard>
    </div>
  );
}
