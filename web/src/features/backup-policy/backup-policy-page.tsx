import { useState } from 'react';
import { Button, Space, Popconfirm, Descriptions, Alert } from 'antd';
import { appMessage } from '@/core/api/notification-bridge';
import {
  CloudUploadOutlined,
  SafetyCertificateOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import {
  useBackupRuns,
  useTriggerBackupRun,
  useTriggerRestoreDrill,
} from './backup-policy-api';
import { BackupRunHistoryTable } from './backup-run-history-table';

// BackupPolicyPage — QĐ 742 §6.1.a backup management UI (NĐ53 compliance)
// Note: no BackupPolicy/schedule entity exists on BE. This page covers:
//   - Static schedule info (Hangfire cron — documented, not API-driven)
//   - Triggered backup + restore-drill runs via existing BackupAdminController
//   - Run history table (polling every 15s for in-progress status updates)
export function BackupPolicyPage() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const [page, setPage] = useState(1);
  const PAGE_SIZE = 50;

  const { data, isFetching, refetch } = useBackupRuns(page, PAGE_SIZE);
  const triggerBackup = useTriggerBackupRun();
  const triggerDrill = useTriggerRestoreDrill();

  const runs = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;

  const filtered = runs.filter(
    (r) =>
      !searchText ||
      [r.type, r.status, r.errorMessage ?? ''].some((v) =>
        v.toLowerCase().includes(searchText.toLowerCase()),
      ),
  );

  async function handleTriggerBackup() {
    try {
      await triggerBackup.mutateAsync();
      appMessage.success(t('backupPolicy.backupStarted', 'Đã khởi động sao lưu'));
    } catch {
      appMessage.error(t('backupPolicy.backupError', 'Khởi động sao lưu thất bại'));
    }
  }

  async function handleTriggerDrill() {
    try {
      await triggerDrill.mutateAsync();
      appMessage.success(t('backupPolicy.drillStarted', 'Đã khởi động diễn tập khôi phục'));
    } catch {
      appMessage.error(t('backupPolicy.drillError', 'Khởi động diễn tập khôi phục thất bại'));
    }
  }

  // Derive simple stats from run history
  const completedRuns = runs.filter((r) => r.status === 'Completed');
  const failedRuns = runs.filter((r) => r.status === 'Failed');
  const lastBackup = runs
    .filter((r) => r.type === 'Backup' && r.status === 'Completed')
    .sort((a, b) => (b.startedAt > a.startedAt ? 1 : -1))[0];
  const lastDrill = runs
    .filter((r) => r.type === 'RestoreDrill' && r.status === 'Completed')
    .sort((a, b) => (b.startedAt > a.startedAt ? 1 : -1))[0];

  const hasPendingOrInProgress = runs.some(
    (r) => r.status === 'Pending' || r.status === 'InProgress',
  );

  return (
    <div>
      <AdminPageHeader
        title={t('backupPolicy.title', 'Quản lý sao lưu & Khôi phục (QĐ 742 §6.1.a)')}
        description={t(
          'backupPolicy.description',
          'Kích hoạt sao lưu thủ công hoặc diễn tập khôi phục. Lịch tự động qua Hangfire (NĐ 53 compliance).',
        )}
        stats={{ total: totalCount, label: t('backupPolicy.runLabel', 'lần thực hiện') }}
        actions={
          <Space wrap>
            <Popconfirm
              title={t('backupPolicy.confirmBackup', 'Bắt đầu sao lưu toàn bộ CSDL?')}
              onConfirm={handleTriggerBackup}
              okText={t('common.confirm', 'Xác nhận')}
              cancelText={t('common.cancel', 'Hủy')}
            >
              <Button
                type="primary"
                icon={<CloudUploadOutlined />}
                loading={triggerBackup.isPending}
              >
                {t('backupPolicy.triggerBackup', 'Sao lưu ngay')}
              </Button>
            </Popconfirm>
            <Popconfirm
              title={t(
                'backupPolicy.confirmDrill',
                'Bắt đầu diễn tập khôi phục? (sao lưu → khôi phục vào DB tạm → kiểm tra → dọn dẹp)',
              )}
              onConfirm={handleTriggerDrill}
              okText={t('common.confirm', 'Xác nhận')}
              cancelText={t('common.cancel', 'Hủy')}
            >
              <Button icon={<SafetyCertificateOutlined />} loading={triggerDrill.isPending}>
                {t('backupPolicy.triggerDrill', 'Diễn tập khôi phục')}
              </Button>
            </Popconfirm>
            <Button icon={<ReloadOutlined />} onClick={() => refetch()} loading={isFetching}>
              {t('common.refresh', 'Làm mới')}
            </Button>
          </Space>
        }
      />

      {/* Run status summary — shows last successful backup + drill timestamps */}
      <div style={{ marginBottom: 16 }}>
      <AdminContentCard>
        <Descriptions size="small" column={{ xs: 1, sm: 2, md: 4 }}>
          <Descriptions.Item label={t('backupPolicy.stat.lastBackup', 'Sao lưu gần nhất')}>
            {lastBackup
              ? new Date(lastBackup.startedAt).toLocaleString('vi-VN')
              : t('backupPolicy.stat.never', 'Chưa có')}
          </Descriptions.Item>
          <Descriptions.Item label={t('backupPolicy.stat.lastDrill', 'Diễn tập gần nhất')}>
            {lastDrill
              ? new Date(lastDrill.startedAt).toLocaleString('vi-VN')
              : t('backupPolicy.stat.never', 'Chưa có')}
          </Descriptions.Item>
          <Descriptions.Item label={t('backupPolicy.stat.successCount', 'Thành công')}>
            {completedRuns.length}
          </Descriptions.Item>
          <Descriptions.Item label={t('backupPolicy.stat.failCount', 'Thất bại')}>
            <span style={{ color: failedRuns.length > 0 ? 'var(--ant-color-error, #ff4d4f)' : undefined }}>
              {failedRuns.length}
            </span>
          </Descriptions.Item>
        </Descriptions>
      </AdminContentCard>
      </div>

      {hasPendingOrInProgress && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          message={t(
            'backupPolicy.inProgressAlert',
            'Có tác vụ đang chạy. Trang tự động làm mới mỗi 15 giây.',
          )}
        />
      )}

      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('backupPolicy.searchPlaceholder', 'Tìm theo loại hoặc trạng thái...')}
          searchValue={searchText}
          onSearchChange={(v) => { setSearchText(v); setPage(1); }}
        />
        <BackupRunHistoryTable runs={filtered} loading={isFetching} pageSize={PAGE_SIZE} />
      </AdminContentCard>
    </div>
  );
}
