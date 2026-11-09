import { useState } from 'react';
import { Tabs, Table, Tag } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { useJobs, useFailedJobs, type JobSummaryDto } from './jobs-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const STATUS_COLOR: Record<string, string> = {
  Recurring: 'blue',
  Idle: 'default',
  Failed: 'red',
  Pending: 'orange',
  Processing: 'processing',
};

// Shared columns for job tables
function useJobColumns() {
  const { t } = useTranslation();

  const columns: ColumnsType<JobSummaryDto> = [
    { title: t('jobs.col.name', { defaultValue: 'Tên' }), dataIndex: 'name', key: 'name', ellipsis: true },
    {
      title: t('jobs.col.cron', { defaultValue: 'Lịch trình' }),
      dataIndex: 'cron',
      key: 'cron',
      width: 160,
      render: (v?: string) => v ?? '—',
    },
    {
      title: t('jobs.col.status', { defaultValue: 'Trạng thái' }),
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (v: string) => <Tag color={STATUS_COLOR[v] ?? 'default'}>{v}</Tag>,
    },
    {
      title: t('jobs.col.lastRun', { defaultValue: 'Lần chạy cuối' }),
      dataIndex: 'lastExecution',
      key: 'lastExecution',
      width: 160,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      title: t('jobs.col.nextRun', { defaultValue: 'Lần chạy tiếp' }),
      dataIndex: 'nextExecution',
      key: 'nextExecution',
      width: 160,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
  ];

  return columns;
}

// ─── All jobs tab (recurring + failed) ───────────────────────────────────────

function AllJobsTab() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const { data, isLoading } = useJobs();
  const columns = useJobColumns();

  const items = data?.items ?? [];
  const filtered = items.filter(item =>
    !searchText || Object.values(item).some(v =>
      String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
    )
  );

  return (
    <>
      <AdminTableToolbar
        searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
        searchValue={searchText}
        onSearchChange={setSearchText}
      />
      <Table<JobSummaryDto>
        rowKey="id"
        columns={columns}
        dataSource={filtered}
        loading={isLoading}
        size="small"
        pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
        scroll={{ x: 800 }}
      />
    </>
  );
}

// ─── Failed jobs tab ─────────────────────────────────────────────────────────

function FailedJobsTab() {
  const { t } = useTranslation();
  const { data, isLoading } = useFailedJobs();

  const items = data?.items ?? [];

  const columns: ColumnsType<JobSummaryDto> = [
    { title: t('jobs.col.name', { defaultValue: 'Tên' }), dataIndex: 'name', key: 'name', ellipsis: true },
    {
      title: t('jobs.col.status', { defaultValue: 'Trạng thái' }),
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: () => <Tag color="red">Failed</Tag>,
    },
    {
      title: t('jobs.col.failedAt', { defaultValue: 'Thời gian lỗi' }),
      dataIndex: 'lastExecution',
      key: 'lastExecution',
      width: 160,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      title: t('jobs.col.createdAt', { defaultValue: 'Ngày tạo' }),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 160,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
  ];

  return (
    <Table<JobSummaryDto>
      rowKey="id"
      columns={columns}
      dataSource={items}
      loading={isLoading}
      size="small"
      pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
      scroll={{ x: 600 }}
    />
  );
}

// ─── Main page ───────────────────────────────────────────────────────────────

// JobMonitorPage — tabs for All Jobs and Failed Jobs
export function JobMonitorPage() {
  const { t } = useTranslation();

  return (
    <div>
      <AdminPageHeader title={t('jobs.title', { defaultValue: 'Giám sát tiến trình' })} />
      <AdminContentCard noPadding>
        <Tabs
          defaultActiveKey="all"
          style={{ padding: '0 24px' }}
          items={[
            { key: 'all', label: t('jobs.tab.all', { defaultValue: 'Tất cả' }), children: <AllJobsTab /> },
            { key: 'failed', label: t('jobs.tab.failed', { defaultValue: 'Lỗi' }), children: <FailedJobsTab /> },
          ]}
        />
      </AdminContentCard>
    </div>
  );
}
