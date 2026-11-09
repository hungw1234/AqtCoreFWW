import { useState } from 'react';
import { Table, Tag, Button, Space, Popconfirm } from 'antd';
import { CloudUploadOutlined, SafetyCertificateOutlined, ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useBackupRecords, useTriggerBackup, useTriggerRestoreDrill, type BackupRecordDto } from './backup-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';

const STATUS_COLORS: Record<string, string> = {
  Pending: 'default',
  InProgress: 'processing',
  Completed: 'success',
  Failed: 'error',
};

function formatBytes(bytes: number | null): string {
  if (bytes === null || bytes === 0) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1048576).toFixed(1)} MB`;
}

// BackupAdminPage — NĐ53 backup/restore admin with history table
export function BackupAdminPage() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  // Fetch all data — client-side search needs full dataset
  const { data, isFetching, refetch } = useBackupRecords(1, 9999);
  const triggerBackup = useTriggerBackup();
  const triggerDrill = useTriggerRestoreDrill();

  async function handleBackup() {
    try {
      await triggerBackup.mutateAsync();
      appMessage.success(t('backup.backupStarted'));
    } catch {
      appMessage.error(t('backup.backupError', 'Khởi động backup thất bại'));
    }
  }

  async function handleDrill() {
    try {
      await triggerDrill.mutateAsync();
      appMessage.success(t('backup.drillStarted'));
    } catch {
      appMessage.error(t('backup.drillError', 'Khởi động kiểm thử khôi phục thất bại'));
    }
  }

  const columns: ColumnsType<BackupRecordDto> = [
    {
      title: t('backup.col.type'),
      dataIndex: 'type',
      key: 'type',
      width: 120,
      render: (v: string) => (
        <Tag color={v === 'Backup' ? 'blue' : 'purple'}>
          {v === 'Backup' ? t('backup.typeBackup') : t('backup.typeDrill')}
        </Tag>
      ),
    },
    {
      title: t('backup.col.status'),
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (v: string) => <Tag color={STATUS_COLORS[v] ?? 'default'}>{v}</Tag>,
    },
    {
      title: t('backup.col.size'),
      dataIndex: 'fileSizeBytes',
      key: 'fileSizeBytes',
      width: 100,
      render: formatBytes,
    },
    {
      title: t('backup.col.startedAt'),
      dataIndex: 'startedAt',
      key: 'startedAt',
      width: 160,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm:ss'),
    },
    {
      title: t('backup.col.completedAt'),
      dataIndex: 'completedAt',
      key: 'completedAt',
      width: 160,
      render: (v: string | null) => v ? dayjs(v).format('DD/MM/YYYY HH:mm:ss') : '—',
    },
    {
      title: t('backup.col.error'),
      dataIndex: 'errorMessage',
      key: 'errorMessage',
      ellipsis: true,
      responsive: ['md'] as const,
      render: (v: string | null) => v ?? '—',
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('backup.title')}
        actions={
          <Space wrap>
            <Popconfirm
              title={t('backup.confirmBackup')}
              onConfirm={handleBackup}
              okText={t('common.confirm')}
              cancelText={t('common.cancel')}
            >
              <Button type="primary" icon={<CloudUploadOutlined />} loading={triggerBackup.isPending}>
                {t('backup.triggerBackup')}
              </Button>
            </Popconfirm>
            <Popconfirm
              title={t('backup.confirmDrill')}
              onConfirm={handleDrill}
              okText={t('common.confirm')}
              cancelText={t('common.cancel')}
            >
              <Button icon={<SafetyCertificateOutlined />} loading={triggerDrill.isPending}>
                {t('backup.triggerDrill')}
              </Button>
            </Popconfirm>
            <Button icon={<ReloadOutlined />} onClick={() => refetch()}>
              {t('webhooks.refresh')}
            </Button>
          </Space>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        <Table<BackupRecordDto>
          rowKey="id"
          columns={columns}
          dataSource={(data?.items ?? []).filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isFetching}
          size="small"
          scroll={{ x: 700 }}

          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
          locale={{ emptyText: t('backup.noRecords') }}
        />
      </AdminContentCard>
    </div>
  );
}
