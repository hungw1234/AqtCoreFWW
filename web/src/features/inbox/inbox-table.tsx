// inbox-table.tsx — View 1: pending workflow tasks table, extracted from inbox-page.tsx
// Reused inside the 2-tab "Hồ sơ của tôi" page as the "Chờ xử lý" tab.

import { Table, Tag, Button, Space, Tooltip } from 'antd';
import { ArrowRightOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useMyInbox } from './inbox-api';
import type { WorkflowTaskDto } from './inbox-api';

// Status colour map for Open/InProgress task states
const STATUS_COLORS: Record<string, string> = {
  Open: 'blue',
  InProgress: 'processing',
};

// Priority colour map: BE enum Low|Normal|High|Urgent
const PRIORITY_COLORS: Record<string, string> = {
  Low: 'default',
  Normal: 'blue',
  High: 'orange',
  Urgent: 'red',
};

export function InboxTable() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data, isFetching } = useMyInbox();
  const tasks = data ?? [];

  const COLUMNS: ColumnsType<WorkflowTaskDto> = [
    {
      title: t('page.inbox.col.title'),
      dataIndex: 'title',
      key: 'title',
      ellipsis: true,
    },
    {
      title: t('page.inbox.col.status'),
      dataIndex: 'status',
      key: 'status',
      width: 130,
      render: (v: string) => <Tag color={STATUS_COLORS[v] ?? 'default'}>{v}</Tag>,
    },
    {
      title: t('page.inbox.col.priority'),
      dataIndex: 'priority',
      key: 'priority',
      width: 110,
      render: (v: string) => <Tag color={PRIORITY_COLORS[v] ?? 'default'}>{v}</Tag>,
    },
    {
      title: t('page.inbox.col.assignee'),
      dataIndex: 'assigneeId',
      key: 'assigneeId',
      width: 130,
      render: (v: string | null) =>
        v ? (
          <Tag color="cyan">{t('page.inbox.assigned')}</Tag>
        ) : (
          <Tag color="orange">{t('page.inbox.poolTask')}</Tag>
        ),
    },
    {
      title: t('page.inbox.col.dueDate'),
      dataIndex: 'dueDate',
      key: 'dueDate',
      width: 140,
      render: (v?: string | null) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      title: t('page.inbox.col.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 140,
      render: (v?: string | null) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      title: t('page.inbox.col.actions'),
      key: 'actions',
      width: 100,
      render: (_, record) => (
        <Space>
          <Tooltip title={t('page.inbox.process', 'Xử lý')}>
            <Button
              size="small"
              type="primary"
              icon={<ArrowRightOutlined />}
              onClick={() =>
                navigate({
                  to: '/business-process-runs/$id',
                  params: { id: record.workflowInstanceId },
                })
              }
            />
          </Tooltip>
        </Space>
      ),
    },
  ];

  return (
    <Table<WorkflowTaskDto>
      rowKey="id"
      columns={COLUMNS}
      dataSource={tasks}
      loading={isFetching}
      size="small"
      scroll={{ x: 900 }}
      locale={{ emptyText: t('page.inbox.empty') }}
      pagination={false}
    />
  );
}
