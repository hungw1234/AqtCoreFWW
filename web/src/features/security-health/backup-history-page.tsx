// backup-history-page.tsx — QĐ 742 §6.1.b D7 visibility: paginated backup/restore-drill history.
import { Alert, Spin, Table, Tag, Typography } from 'antd';
import { DatabaseOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useBackupHistoryStatus } from './security-health-api';
import type { BackupHistoryItemDto } from './security-health-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

const STATUS_COLORS: Record<string, string> = {
  Pending: 'default',
  InProgress: 'processing',
  Completed: 'success',
  Failed: 'error',
};

const TYPE_COLORS: Record<string, string> = {
  Backup: 'blue',
  RestoreDrill: 'purple',
};

function formatBytes(bytes: number | null): string {
  if (bytes === null || bytes === 0) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1_048_576) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1_048_576).toFixed(1)} MB`;
}

// BackupHistoryPage — D7 compliance snapshot for QĐ 742 §6.1.b backup + restore-drill history.
export function BackupHistoryPage() {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const PAGE_SIZE = 20;

  const { data, isLoading, isError, dataUpdatedAt } = useBackupHistoryStatus(page, PAGE_SIZE);

  const total = data?.totalCount ?? 0;

  const columns: ColumnsType<BackupHistoryItemDto> = [
    {
      title: t('securityHealth.backupHistory.col.type', 'Loại'),
      dataIndex: 'type',
      key: 'type',
      width: 130,
      render: (v: string) => (
        <Tag color={TYPE_COLORS[v] ?? 'default'}>
          {v === 'Backup'
            ? t('backup.typeBackup', 'Sao lưu')
            : t('securityHealth.backupHistory.typeDrill', 'Diễn tập khôi phục')}
        </Tag>
      ),
    },
    {
      title: t('securityHealth.backupHistory.col.status', 'Trạng thái'),
      dataIndex: 'status',
      key: 'status',
      width: 120,
      render: (v: string) => <Tag color={STATUS_COLORS[v] ?? 'default'}>{v}</Tag>,
    },
    {
      title: t('securityHealth.backupHistory.col.startedAt', 'Bắt đầu'),
      dataIndex: 'startedAt',
      key: 'startedAt',
      width: 165,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm:ss'),
    },
    {
      title: t('securityHealth.backupHistory.col.completedAt', 'Hoàn thành'),
      dataIndex: 'completedAt',
      key: 'completedAt',
      width: 165,
      render: (v: string | null) => v ? dayjs(v).format('DD/MM/YYYY HH:mm:ss') : '—',
    },
    {
      title: t('securityHealth.backupHistory.col.fileSize', 'Dung lượng'),
      dataIndex: 'fileSizeBytes',
      key: 'fileSizeBytes',
      width: 110,
      render: formatBytes,
    },
    {
      title: t('securityHealth.backupHistory.col.filePath', 'Đường dẫn'),
      dataIndex: 'filePath',
      key: 'filePath',
      ellipsis: true,
      responsive: ['lg'] as const,
      render: (v: string | null) => v ?? '—',
    },
    {
      title: t('securityHealth.backupHistory.col.errorMessage', 'Lỗi'),
      dataIndex: 'errorMessage',
      key: 'errorMessage',
      ellipsis: true,
      responsive: ['md'] as const,
      render: (v: string | null) =>
        v ? <Text type="danger" ellipsis={{ tooltip: v }}>{v}</Text> : '—',
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('securityHealth.backupHistory.title', 'Lịch sử sao lưu & Diễn tập khôi phục')}
        icon={<DatabaseOutlined />}
        description={t(
          'securityHealth.backupHistory.qd742Wording',
          'Sao lưu định kỳ và kiểm tra phục hồi (có lịch sử sao lưu/khôi phục)',
        )}
        stats={{ total: total ?? 0, label: t('securityHealth.backupHistory.records', 'bản ghi') }}
      />

      <AdminContentCard noPadding>
        {isLoading && (
          <div style={{ textAlign: 'center', padding: 48 }}>
            <Spin size="large" />
          </div>
        )}

        {isError && !isLoading && (
          <Alert
            type="error"
            showIcon
            style={{ margin: 24 }}
            message={t('common.error', 'Lỗi')}
            description={t('securityHealth.loadError', 'Không thể tải dữ liệu. Vui lòng thử lại.')}
          />
        )}

        {!isLoading && !isError && (
          <Table<BackupHistoryItemDto>
            rowKey="id"
            columns={columns}
            dataSource={data?.items ?? []}
            loading={isLoading}
            size="small"
            scroll={{ x: 800 }}
            locale={{ emptyText: t('backup.noRecords', 'Chưa có bản ghi sao lưu') }}
            pagination={{
              current: page,
              pageSize: PAGE_SIZE,
              total: total ?? 0,
              showSizeChanger: false,
              onChange: (p) => setPage(p),
              showTotal: (tot) =>
                t('common.pagination.showTotal', { total: tot, defaultValue: `Tổng ${tot} bản ghi` }),
            }}
          />
        )}

        {dataUpdatedAt > 0 && (
          <Text
            type="secondary"
            style={{ display: 'block', padding: '8px 24px 16px', fontSize: 12 }}
          >
            {t('securityHealth.lastRefreshedAt', 'Cập nhật lúc')}:{' '}
            {dayjs(dataUpdatedAt).format('DD/MM/YYYY HH:mm:ss')}
          </Text>
        )}
      </AdminContentCard>
    </div>
  );
}
