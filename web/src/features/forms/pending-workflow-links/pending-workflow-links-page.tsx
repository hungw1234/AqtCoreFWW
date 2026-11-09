import { useState } from 'react';
import { Table, Button, Space, Tag, Select, Tooltip, Typography } from 'antd';
import { ReloadOutlined, InboxOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { appMessage } from '@/core/api/notification-bridge';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { usePermissions } from '@/features/auth';
import {
  usePendingWorkflowLinks,
  useRetryWorkflowLinkNow,
  useShelveWorkflowLink,
} from './pending-workflow-links-api';
import type {
  PendingWorkflowLinkListItemDto,
  WorkflowLinkPendingStatus,
} from './pending-workflow-links-types';

// Permission literal — MUST byte-match BE PermCodes.Forms.WorkflowLink.Manage (LOCKED Q3).
const MANAGE_PERM = 'FORMS.WFLINK.MANAGE';

const STATUS_COLORS: Record<WorkflowLinkPendingStatus, string> = {
  Pending: 'gold',
  Retrying: 'blue',
  Succeeded: 'green',
  Shelved: 'red',
};

// Retry-now applies to non-terminal rows + un-shelve (Shelved). Succeeded is terminal → no action.
const RETRYABLE: WorkflowLinkPendingStatus[] = ['Pending', 'Retrying', 'Shelved'];
// Shelve terminalizes a still-active row.
const SHELVABLE: WorkflowLinkPendingStatus[] = ['Pending', 'Retrying'];

/**
 * Admin "Pending Workflow Links" page (P6-DEFER-1 phase 06).
 * Lists orphan FormSubmission→Workflow link attempts (all 4 statuses) with manual
 * Retry-now / Shelve overrides. Gated by FORMS.WFLINK.MANAGE.
 */
export function PendingWorkflowLinksPage() {
  const { t } = useTranslation();
  const { hasPermission } = usePermissions();
  const [status, setStatus] = useState<string | undefined>(undefined);
  const { page, pageSize, antPagination } = useServerPagination(20, [status]);

  const { data, isFetching } = usePendingWorkflowLinks(status, page, pageSize);
  const retryMutation = useRetryWorkflowLinkNow();
  const shelveMutation = useShelveWorkflowLink();

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  function handleRetry(id: string) {
    retryMutation.mutate(id, {
      onSuccess: () => appMessage.success(t('pendingWorkflowLinks.retryQueued', { defaultValue: 'Đã xếp hàng thử lại' })),
      onError: () => appMessage.error(t('common.error', { defaultValue: 'Thao tác thất bại' })),
    });
  }

  function handleShelve(id: string) {
    shelveMutation.mutate(id, {
      onSuccess: () => appMessage.success(t('pendingWorkflowLinks.shelved', { defaultValue: 'Đã xếp kho' })),
      onError: () => appMessage.error(t('common.error', { defaultValue: 'Thao tác thất bại' })),
    });
  }

  const canManage = hasPermission(MANAGE_PERM);

  const columns: ColumnsType<PendingWorkflowLinkListItemDto> = [
    {
      title: t('pendingWorkflowLinks.col.template', { defaultValue: 'Biểu mẫu' }),
      dataIndex: 'templateName',
      key: 'templateName',
      ellipsis: true,
      render: (v: string) => v || '—',
    },
    {
      title: t('pendingWorkflowLinks.col.status', { defaultValue: 'Trạng thái' }),
      dataIndex: 'status',
      key: 'status',
      width: 120,
      render: (v: WorkflowLinkPendingStatus) => (
        <Tag color={STATUS_COLORS[v] ?? 'default'}>
          {t(`pendingWorkflowLinks.status.${v}`, { defaultValue: v })}
        </Tag>
      ),
    },
    {
      title: t('pendingWorkflowLinks.col.attempts', { defaultValue: 'Số lần thử' }),
      dataIndex: 'attemptCount',
      key: 'attemptCount',
      width: 100,
      align: 'center',
    },
    {
      title: t('pendingWorkflowLinks.col.lastFailure', { defaultValue: 'Lỗi gần nhất' }),
      dataIndex: 'lastFailureCode',
      key: 'lastFailureCode',
      width: 170,
      ellipsis: true,
      render: (code: string, row) =>
        code ? <Tooltip title={row.lastError ?? code}><Typography.Text code>{code}</Typography.Text></Tooltip> : '—',
    },
    {
      title: t('pendingWorkflowLinks.col.nextRetryAt', { defaultValue: 'Lần thử kế' }),
      dataIndex: 'nextRetryAt',
      key: 'nextRetryAt',
      width: 150,
      render: (v?: string | null) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      title: '',
      key: 'actions',
      width: 200,
      render: (_, row) => (
        <Space size="small">
          <Button
            size="small"
            icon={<ReloadOutlined />}
            disabled={!canManage || !RETRYABLE.includes(row.status)}
            loading={retryMutation.isPending && retryMutation.variables === row.id}
            onClick={() => handleRetry(row.id)}
          >
            {t('pendingWorkflowLinks.retryNow', { defaultValue: 'Thử lại ngay' })}
          </Button>
          <Button
            size="small"
            icon={<InboxOutlined />}
            disabled={!canManage || !SHELVABLE.includes(row.status)}
            loading={shelveMutation.isPending && shelveMutation.variables === row.id}
            onClick={() => handleShelve(row.id)}
          >
            {t('pendingWorkflowLinks.shelve', { defaultValue: 'Xếp kho' })}
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('pendingWorkflowLinks.title', { defaultValue: 'Liên kết quy trình đang chờ' })}
        stats={{ total, label: t('common.items', { defaultValue: 'mục' }) }}
        actions={
          <Select
            allowClear
            style={{ width: 180 }}
            placeholder={t('pendingWorkflowLinks.filterStatus', { defaultValue: 'Lọc theo trạng thái' })}
            value={status}
            onChange={(v) => setStatus(v)}
            options={(['Pending', 'Retrying', 'Succeeded', 'Shelved'] as const).map((s) => ({
              value: s,
              label: t(`pendingWorkflowLinks.status.${s}`, { defaultValue: s }),
            }))}
          />
        }
      />
      <AdminContentCard noPadding>
        <Table<PendingWorkflowLinkListItemDto>
          rowKey="id"
          columns={columns}
          dataSource={items}
          loading={isFetching}
          size="small"
          scroll={{ x: 800 }}
          pagination={{ ...antPagination, total }}
        />
      </AdminContentCard>
    </div>
  );
}
