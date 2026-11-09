// workflow-version-history-panel — shows all versions of a definition with migrate actions
// Used as a tab inside WorkflowDefinitionDetailPage.

import { Table, Tag, Button, Tooltip } from 'antd';
import { SwapOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useVersionHistory } from '../workflow-migration-api';
import { usePermissions } from '@/features/auth';
import { WorkflowPermissions } from '../workflow-permissions';
import type { VersionHistoryDto } from '../workflow-types';

interface Props {
  /** BE definition key (e.g. "case-approval") — used for API calls */
  definitionKey: string;
  /** Called when user clicks "Migrate from here" — passes sourceDefinitionId */
  onMigrateFrom: (sourceDefId: string) => void;
}

export function WorkflowVersionHistoryPanel({ definitionKey, onMigrateFrom }: Props) {
  const { t } = useTranslation();
  const { hasPermission } = usePermissions();
  const canCreate = hasPermission(WorkflowPermissions.MigrationCreate);

  const { data = [], isLoading } = useVersionHistory(definitionKey);

  const columns: ColumnsType<VersionHistoryDto> = [
    {
      title: t('workflow.migration.field.version'),
      dataIndex: 'version',
      key: 'version',
      sorter: (a, b) => a.version - b.version,
    },
    {
      title: t('workflow.migration.field.isLatest'),
      dataIndex: 'isLatest',
      key: 'isLatest',
      render: (v: boolean) =>
        v ? <Tag color="blue">{t('workflow.isLatest')}</Tag> : null,
    },
    {
      title: t('workflow.status'),
      dataIndex: 'isActive',
      key: 'isActive',
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>
          {v ? t('workflow.active') : t('workflow.inactive')}
        </Tag>
      ),
    },
    {
      title: t('workflow.col.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (v: string) => dayjs(v).format('YYYY-MM-DD HH:mm'),
    },
    {
      title: t('workflow.migration.field.instanceCount'),
      dataIndex: 'instanceCount',
      key: 'instanceCount',
    },
    {
      title: '',
      key: 'actions',
      render: (_: unknown, record: VersionHistoryDto) =>
        canCreate && !record.isLatest ? (
          <Tooltip title={t('workflow.migration.action.create')}>
            <Button
              size="small"
              icon={<SwapOutlined />}
              onClick={() => onMigrateFrom(record.id)}
            >
              {t('workflow.migration.action.create')}
            </Button>
          </Tooltip>
        ) : null,
    },
  ];

  return (
    <Table<VersionHistoryDto>
      rowKey="id"
      columns={columns}
      dataSource={data}
      loading={isLoading}
      pagination={false}
      size="small"
    />
  );
}
