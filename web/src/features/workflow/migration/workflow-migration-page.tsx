// workflow-migration-page — container for migration list + detail.
// Shows list when no migrationId in props, status panel when migrationId provided.
// "New Migration" button opens wizard modal.

import { useState } from 'react';
import {
  Button, Table, Tag, Space, Modal, Typography, Select, Spin,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import dayjs from 'dayjs';
import { usePermissions } from '@/features/auth';
import { WorkflowPermissions } from '../workflow-permissions';
import { useMigrationsByDef, useVersionHistory } from '../workflow-migration-api';
import { WorkflowMigrationStatusPanel } from './workflow-migration-status-panel';
import { WorkflowStateMappingWizard } from './workflow-state-mapping-wizard';
import type { MigrationDto, MigrationStatus } from '../workflow-types';

const { Title } = Typography;

const STATUS_COLOR: Record<string, string> = {
  Draft: 'default', PendingApproval: 'orange', Approved: 'blue',
  InProgress: 'processing', Completed: 'success', Failed: 'error', Cancelled: 'default',
};

interface Props {
  /** BE definition UUID (used to load definition data) */
  definitionId: string;
  /** BE definition key (used for migration API calls) */
  definitionKey: string;
  /** If provided, show status panel for this migration instead of list */
  migrationId?: string;
}

export function WorkflowMigrationPage({ definitionId, definitionKey, migrationId }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { hasPermission } = usePermissions();
  const canCreate = hasPermission(WorkflowPermissions.MigrationCreate);

  const [wizardOpen, setWizardOpen] = useState(false);
  const [sourceDefId, setSourceDefId] = useState<string | undefined>(undefined);

  const { data: migrations = [], isLoading } = useMigrationsByDef(definitionKey);
  const { data: versions = [], isLoading: versionsLoading } = useVersionHistory(definitionKey);

  // If migrationId provided — show status panel
  if (migrationId) {
    return (
      <Space direction="vertical" style={{ width: '100%' }}>
        <Button onClick={() => navigate({ to: '/admin/workflow/$definitionId', params: { definitionId } })}>
          ← {t('common.back')}
        </Button>
        <WorkflowMigrationStatusPanel
          migrationId={migrationId}
          definitionKey={definitionKey}
        />
      </Space>
    );
  }

  const columns: ColumnsType<MigrationDto> = [
    {
      title: 'ID',
      dataIndex: 'id',
      key: 'id',
      render: (v: string) => <Typography.Text code>{v.slice(0, 8)}…</Typography.Text>,
    },
    {
      title: t('workflow.status'),
      dataIndex: 'status',
      key: 'status',
      render: (v: MigrationStatus) => (
        <Tag color={STATUS_COLOR[v]}>
          {t(`workflow.migration.status.${v.charAt(0).toLowerCase() + v.slice(1)}`)}
        </Tag>
      ),
    },
    {
      title: t('workflow.migration.field.mappedCount'),
      key: 'counts',
      render: (_: unknown, r: MigrationDto) =>
        `${r.migratedCount} / ${r.totalInstances}`,
    },
    {
      title: t('workflow.col.createdAt'),
      dataIndex: 'startedAt',
      key: 'startedAt',
      render: (v?: string) => v ? dayjs(v).format('YYYY-MM-DD HH:mm') : '—',
    },
    {
      title: '',
      key: 'actions',
      render: (_: unknown, r: MigrationDto) => (
        <Button
          size="small"
          onClick={() =>
            navigate({
              to: '/admin/workflow/$definitionId/migrations/$migrationId',
              params: { definitionId, migrationId: r.id },
            })
          }
        >
          {t('workflow.migration.action.viewItems')}
        </Button>
      ),
    },
  ];

  const latestVersion = versions.find((v) => v.isLatest);

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Space style={{ justifyContent: 'space-between', width: '100%' }}>
        <Title level={5} style={{ margin: 0 }}>
          {t('workflow.migration.action.viewItems')}
        </Title>
        {canCreate && (
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => {
              setSourceDefId(undefined);
              setWizardOpen(true);
            }}
          >
            {t('workflow.migration.action.create')}
          </Button>
        )}
      </Space>

      <Table<MigrationDto>
        rowKey="id"
        columns={columns}
        dataSource={migrations}
        loading={isLoading}
        size="small"
        pagination={{ pageSize: 20 }}
      />

      {/* Wizard modal */}
      <Modal
        open={wizardOpen}
        title={t('workflow.migration.action.create')}
        onCancel={() => setWizardOpen(false)}
        footer={null}
        width={700}
        destroyOnClose
      >
        {versionsLoading ? (
          <Spin />
        ) : (
          <Space direction="vertical" style={{ width: '100%' }}>
            {!sourceDefId && (
              <Select
                style={{ width: '100%' }}
                placeholder={t('workflow.migration.field.version')}
                options={versions
                  .filter((v) => !v.isLatest)
                  .map((v) => ({
                    label: `v${v.version} (${v.instanceCount} instances)`,
                    value: v.id,
                  }))}
                onChange={(val) => setSourceDefId(val)}
              />
            )}
            {sourceDefId && latestVersion && (
              <WorkflowStateMappingWizard
                sourceDefinitionId={sourceDefId}
                targetDefinitionId={latestVersion.id}
                definitionKey={definitionKey}
                onCreated={(migration) => {
                  setWizardOpen(false);
                  navigate({
                    to: '/admin/workflow/$definitionId/migrations/$migrationId',
                    params: { definitionId, migrationId: migration.id },
                  });
                }}
              />
            )}
          </Space>
        )}
      </Modal>
    </Space>
  );
}
