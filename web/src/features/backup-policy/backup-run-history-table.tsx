import { Table, Tag, Tooltip } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { BackupRunDto } from './backup-policy-api';

// Status → Antd tag color mapping
const STATUS_COLORS: Record<string, string> = {
  Pending: 'default',
  InProgress: 'processing',
  Completed: 'success',
  Failed: 'error',
};

function formatBytes(bytes?: number): string {
  if (bytes == null || bytes === 0) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1_048_576) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1_048_576).toFixed(1)} MB`;
}

function formatDuration(startedAt: string, completedAt?: string): string {
  if (!completedAt) return '—';
  const diffMs = dayjs(completedAt).diff(dayjs(startedAt));
  if (diffMs < 1000) return `${diffMs}ms`;
  if (diffMs < 60_000) return `${(diffMs / 1000).toFixed(1)}s`;
  return `${Math.floor(diffMs / 60_000)}m ${Math.round((diffMs % 60_000) / 1000)}s`;
}

interface BackupRunHistoryTableProps {
  runs: BackupRunDto[];
  loading?: boolean;
  pageSize?: number;
}

// BackupRunHistoryTable — reusable sub-table for backup/restore-drill run history.
// Used by BackupPolicyPage and (optionally) the D7 security-health backup-history page.
export function BackupRunHistoryTable({
  runs,
  loading,
  pageSize = 20,
}: BackupRunHistoryTableProps) {
  const { t } = useTranslation();

  const columns: ColumnsType<BackupRunDto> = [
    {
      title: t('backupPolicy.col.type', 'Loại'),
      dataIndex: 'type',
      key: 'type',
      width: 130,
      render: (v: string) => (
        <Tag color={v === 'Backup' ? 'blue' : 'purple'}>
          {v === 'Backup'
            ? t('backupPolicy.typeBackup', 'Sao lưu')
            : t('backupPolicy.typeDrill', 'Diễn tập khôi phục')}
        </Tag>
      ),
    },
    {
      title: t('backupPolicy.col.status', 'Trạng thái'),
      dataIndex: 'status',
      key: 'status',
      width: 120,
      render: (v: string) => (
        <Tag color={STATUS_COLORS[v] ?? 'default'}>{v}</Tag>
      ),
    },
    {
      title: t('backupPolicy.col.size', 'Dung lượng'),
      dataIndex: 'fileSizeBytes',
      key: 'fileSizeBytes',
      width: 110,
      render: (v?: number) => formatBytes(v),
    },
    {
      title: t('backupPolicy.col.startedAt', 'Bắt đầu'),
      dataIndex: 'startedAt',
      key: 'startedAt',
      width: 160,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm:ss'),
      defaultSortOrder: 'descend',
      sorter: (a, b) => dayjs(a.startedAt).unix() - dayjs(b.startedAt).unix(),
    },
    {
      title: t('backupPolicy.col.duration', 'Thời gian'),
      key: 'duration',
      width: 100,
      render: (_, r) => formatDuration(r.startedAt, r.completedAt),
    },
    {
      title: t('backupPolicy.col.error', 'Lỗi'),
      dataIndex: 'errorMessage',
      key: 'errorMessage',
      ellipsis: true,
      render: (v?: string) =>
        v ? (
          <Tooltip title={v}>
            <span style={{ color: 'var(--ant-color-error, #ff4d4f)', cursor: 'help' }}>
              {v.length > 60 ? `${v.slice(0, 60)}…` : v}
            </span>
          </Tooltip>
        ) : (
          '—'
        ),
    },
  ];

  return (
    <Table<BackupRunDto>
      rowKey="id"
      columns={columns}
      dataSource={runs}
      loading={loading}
      size="small"
      scroll={{ x: 700 }}
      pagination={{
        pageSize,
        showSizeChanger: true,
        showTotal: (total) => `${t('common.total', 'Tổng')} ${total}`,
      }}
      locale={{ emptyText: t('backupPolicy.noRuns', 'Chưa có lần sao lưu nào') }}
    />
  );
}
