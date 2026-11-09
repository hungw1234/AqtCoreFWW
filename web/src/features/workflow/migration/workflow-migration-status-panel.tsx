// workflow-migration-status-panel — real-time status view for a single migration.
// Polls every 3s while InProgress (SignalR fallback). Shows action buttons per perm+status.

import { useState } from 'react';
import {
  Space,
  Tag,
  Progress,
  Button,
  Table,
  Typography,
  Popconfirm,
  Alert,
  Segmented,
  Flex,
  Empty,
} from 'antd';
import {
  DatabaseOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
  MinusCircleOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { usePermissions } from '@/features/auth';
import { useAuth } from '@/features/auth';
import { WorkflowPermissions } from '../workflow-permissions';
import {
  useMigration, useMigrationItems,
  useApproveMigration, useExecuteMigration, useCancelMigration,
} from '../workflow-migration-api';
import type { MigrationItemDto, MigrationItemStatus } from '../workflow-types';
import { appMessage } from '@/core/api/notification-bridge';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

// StatTile — compact metric tile (icon + label + value) matching the general-tab look.
// valueColor optionally tints the number (e.g. error/warning counts) via design tokens.
function StatTile({
  icon, label, value, valueColor,
}: { icon: React.ReactNode; label: string; value: React.ReactNode; valueColor?: string }) {
  return (
    <Flex
      vertical
      gap={4}
      style={{
        flex: 1,
        minWidth: 120,
        padding: '14px 16px',
        background: 'var(--gov-bg-muted)',
        border: '1px solid var(--gov-border-color)',
        borderRadius: 'var(--radius-md)',
      }}
    >
      <Flex align="center" gap={6} style={{ color: 'var(--gov-text-secondary)', fontSize: 12 }}>
        <span style={{ color: 'var(--gov-navy)', display: 'flex' }}>{icon}</span>
        {label}
      </Flex>
      <Text strong style={{ fontSize: 20, lineHeight: 1.2, color: valueColor }}>{value}</Text>
    </Flex>
  );
}

const STATUS_COLOR: Record<string, string> = {
  Draft: 'default', PendingApproval: 'orange', Approved: 'blue',
  InProgress: 'processing', Completed: 'success', Failed: 'error', Cancelled: 'default',
};

const ITEM_STATUS_COLOR: Record<string, string> = {
  Pending: 'default', Succeeded: 'success', Failed: 'error', Skipped: 'warning',
};

interface Props {
  migrationId: string;
  definitionKey: string;
}

export function WorkflowMigrationStatusPanel({ migrationId, definitionKey }: Props) {
  const { t } = useTranslation();
  const { hasPermission } = usePermissions();
  const { user } = useAuth();
  const currentUserId = user?.profile?.sub as string | undefined;

  const [page, setPage] = useState(1);
  const [itemFilter, setItemFilter] = useState<MigrationItemStatus | undefined>(undefined);

  const { data: migration, isLoading } = useMigration(migrationId);
  const { data: itemsPage, isLoading: itemsLoading } = useMigrationItems(
    migrationId, page, itemFilter,
  );

  const approveMutation = useApproveMigration(definitionKey);
  const executeMutation = useExecuteMigration(definitionKey);
  const cancelMutation = useCancelMigration(definitionKey);

  if (isLoading || !migration) return <AdminContentCard><Progress percent={0} /></AdminContentCard>;

  const { status, totalInstances, migratedCount, failedCount, skippedCount,
    createdBy, startedAt, completedAt, mappingHash } = migration;

  const canApprove = hasPermission(WorkflowPermissions.MigrationApprove)
    && status === 'PendingApproval'
    && currentUserId !== createdBy;

  const canExecute = hasPermission(WorkflowPermissions.MigrationExecute)
    && status === 'Approved';

  const canCancel = hasPermission(WorkflowPermissions.MigrationCancel)
    && ['PendingApproval', 'Approved', 'InProgress'].includes(status);

  const handleApprove = async () => {
    await approveMutation.mutateAsync(migrationId);
    appMessage.success(t('workflow.migration.action.approve'));
  };

  const handleExecute = async () => {
    await executeMutation.mutateAsync(migrationId);
    appMessage.success(t('workflow.migration.action.execute'));
  };

  const handleCancel = async () => {
    await cancelMutation.mutateAsync(migrationId);
    appMessage.success(t('workflow.migration.action.cancel'));
  };

  const percent = totalInstances > 0
    ? Math.round((migratedCount / totalInstances) * 100)
    : 0;

  const columns: ColumnsType<MigrationItemDto> = [
    { title: 'Instance', dataIndex: 'instanceId', key: 'instanceId',
      render: (v: string) => <Text code style={{ fontSize: 11 }}>{v.slice(0, 8)}…</Text> },
    { title: t('workflow.fromState'), dataIndex: 'oldStateId', key: 'old',
      render: (v: string) => <Text code>{v.slice(0, 8)}</Text> },
    { title: t('workflow.toState'), dataIndex: 'newStateId', key: 'new',
      render: (v: string) => <Text code>{v.slice(0, 8)}</Text> },
    {
      title: t('workflow.status'), dataIndex: 'status', key: 'status',
      render: (v: MigrationItemStatus) => (
        <Tag color={ITEM_STATUS_COLOR[v]}>
          {t(`workflow.migration.itemStatus.${v.charAt(0).toLowerCase() + v.slice(1)}`)}
        </Tag>
      ),
    },
    { title: t('workflow.migration.field.version'), dataIndex: 'errorMessage', key: 'err',
      render: (v?: string) => v ? <Text type="danger" style={{ fontSize: 11 }}>{v}</Text> : null },
    { title: t('workflow.effectiveFrom'), dataIndex: 'processedAt', key: 'proc',
      render: (v?: string) => v ? dayjs(v).format('HH:mm:ss') : '—' },
  ];

  return (
    <AdminContentCard>
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      {/* Header row: status badge + hash + timestamps */}
      <Space wrap>
        <Tag color={STATUS_COLOR[status]}>
          {t(`workflow.migration.status.${status.charAt(0).toLowerCase() + status.slice(1)}`)}
        </Tag>
        <Text type="secondary" style={{ fontSize: 12 }}>
          {t('workflow.migration.field.hash')}: <Text code>{mappingHash.slice(0, 8)}</Text>
        </Text>
        {startedAt && (
          <Text type="secondary" style={{ fontSize: 12 }}>
            {t('workflow.effectiveFrom')}: {dayjs(startedAt).format('YYYY-MM-DD HH:mm')}
          </Text>
        )}
        {completedAt && (
          <Text type="secondary" style={{ fontSize: 12 }}>
            {t('workflow.effectiveUntil')}: {dayjs(completedAt).format('YYYY-MM-DD HH:mm')}
          </Text>
        )}
      </Space>

      {/* Counts — stat tiles matching the general-tab look */}
      <Flex gap={12} wrap>
        <StatTile
          icon={<DatabaseOutlined />}
          label={t('workflow.migration.field.totalCount', { defaultValue: 'Tổng số' })}
          value={totalInstances}
        />
        <StatTile
          icon={<CheckCircleOutlined />}
          label={t('workflow.migration.field.mappedCount')}
          value={migratedCount}
          valueColor="var(--status-success)"
        />
        <StatTile
          icon={<CloseCircleOutlined />}
          label={t('workflow.migration.field.failedCount')}
          value={failedCount}
          valueColor={failedCount > 0 ? 'var(--status-error)' : undefined}
        />
        <StatTile
          icon={<MinusCircleOutlined />}
          label={t('workflow.migration.field.skippedCount', { defaultValue: 'Đã bỏ qua' })}
          value={skippedCount}
          valueColor={skippedCount > 0 ? 'var(--status-warning)' : undefined}
        />
      </Flex>

      {/* Progress bar */}
      <Progress percent={percent} status={status === 'Failed' ? 'exception' : undefined} />

      {/* Action buttons */}
      {(canApprove || canExecute || canCancel) && (
        <Space>
          {canApprove && (
            <Popconfirm title={t('workflow.migration.action.approve') + '?'} onConfirm={handleApprove}>
              <Button type="primary" loading={approveMutation.isPending}>
                {t('workflow.migration.action.approve')}
              </Button>
            </Popconfirm>
          )}
          {canExecute && (
            <Popconfirm title={t('workflow.migration.action.execute') + '?'} onConfirm={handleExecute}>
              <Button type="primary" loading={executeMutation.isPending}>
                {t('workflow.migration.action.execute')}
              </Button>
            </Popconfirm>
          )}
          {canCancel && (
            <Popconfirm title={t('workflow.migration.action.cancel') + '?'} onConfirm={handleCancel}>
              <Button danger loading={cancelMutation.isPending}>
                {t('workflow.migration.action.cancel')}
              </Button>
            </Popconfirm>
          )}
        </Space>
      )}

      {/* Self-approval warning */}
      {status === 'PendingApproval' && currentUserId === createdBy && (
        <Alert type="warning" showIcon message={t('workflow.migration.error.selfApproval')} />
      )}

      {/* Items table with filter chips */}
      <Segmented
        options={[
          { label: t('common.total'), value: '' },
          { label: t('workflow.migration.itemStatus.failed'), value: 'Failed' },
          { label: t('workflow.migration.itemStatus.skipped'), value: 'Skipped' },
        ]}
        value={itemFilter ?? ''}
        onChange={(v) => { setItemFilter(v ? (v as MigrationItemStatus) : undefined); setPage(1); }}
      />

      <Table<MigrationItemDto>
        rowKey="id"
        columns={columns}
        dataSource={itemsPage?.items ?? []}
        loading={itemsLoading}
        size="small"
        pagination={{
          current: page,
          pageSize: 50,
          total: itemsPage?.totalCount,
          onChange: setPage,
          showTotal: (total) => `${total} ${t('common.items')}`,
        }}
        locale={{
          emptyText: (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={t('workflow.migration.emptyItems', { defaultValue: 'Chưa có bản ghi di trú' })}
            />
          ),
        }}
      />
    </Space>
    </AdminContentCard>
  );
}
