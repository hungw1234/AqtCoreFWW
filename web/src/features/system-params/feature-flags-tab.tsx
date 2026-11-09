import { Table, Switch, Slider, Space, Tag } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useFeatureFlags, useUpdateFeatureFlag } from './system-params-api';
import type { FeatureFlagDto } from './system-params-types';
import { appMessage } from '@/core/api/notification-bridge';

// FeatureFlagsTab — toggle feature flags and set rollout percentage
export function FeatureFlagsTab() {
  const { t } = useTranslation();
  const { data: flags = [], isFetching } = useFeatureFlags();
  const updateMutation = useUpdateFeatureFlag();

  async function handleToggle(flag: FeatureFlagDto, isEnabled: boolean) {
    try {
      await updateMutation.mutateAsync({
        id: flag.id,
        body: { isEnabled, rolloutPercentage: flag.rolloutPercentage },
      });
      const state = isEnabled
        ? t('page.admin.systemParams.flags.toggleOn')
        : t('page.admin.systemParams.flags.toggleOff');
      appMessage.success(t('page.admin.systemParams.flags.toggleSuccess', { name: flag.name, state }));
    } catch {
      appMessage.error(t('page.admin.systemParams.flags.updateFail'));
    }
  }

  async function handleRollout(flag: FeatureFlagDto, rolloutPercentage: number) {
    try {
      await updateMutation.mutateAsync({
        id: flag.id,
        body: { isEnabled: flag.isEnabled, rolloutPercentage },
      });
    } catch {
      appMessage.error(t('page.admin.systemParams.flags.updateFail'));
    }
  }

  const COLUMNS: ColumnsType<FeatureFlagDto> = [
    {
      title: t('page.admin.systemParams.flags.colName'),
      dataIndex: 'name',
      key: 'name',
      width: 200,
    },
    {
      title: t('page.admin.systemParams.flags.colKey'),
      dataIndex: 'key',
      key: 'key',
      width: 200,
      render: (v: string) => <code style={{ fontSize: 12 }}>{v}</code>,
    },
    {
      title: t('page.admin.systemParams.flags.colDescription'),
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
    },
    {
      title: t('page.admin.systemParams.flags.colEnabled'),
      dataIndex: 'isEnabled',
      key: 'isEnabled',
      width: 90,
      render: (v: boolean, record) => (
        <Switch
          checked={v}
          onChange={(checked) => handleToggle(record, checked)}
          loading={updateMutation.isPending}
        />
      ),
    },
    {
      title: 'Rollout %',
      dataIndex: 'rolloutPercentage',
      key: 'rolloutPercentage',
      width: 200,
      render: (v: number, record) => (
        <Space style={{ width: '100%' }}>
          <Slider
            min={0}
            max={100}
            defaultValue={v}
            style={{ width: 120 }}
            disabled={!record.isEnabled}
            onChangeComplete={(val) => handleRollout(record, val)}
          />
          <Tag color={v === 100 ? 'green' : v === 0 ? 'default' : 'blue'}>
            {v}%
          </Tag>
        </Space>
      ),
    },
  ];

  return (
    <Table<FeatureFlagDto>
      rowKey="id"
      columns={COLUMNS}
      dataSource={flags}
      loading={isFetching}
      size="small"
      pagination={false}
      locale={{ emptyText: t('common.noData') }}
    />
  );
}
