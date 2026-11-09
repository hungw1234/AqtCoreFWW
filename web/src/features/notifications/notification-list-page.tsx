import { useState } from 'react';
import { Table, Tag, Typography, Button, Popconfirm, Flex } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useNotifications, useMarkAsRead, useMarkAllAsRead } from './notification-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import type { NotificationDto } from './notification-types';
import { appMessage } from '@/core/api/notification-bridge';

const { Text } = Typography;

// NotificationListPage — full paginated notification list with mark-as-read
export function NotificationListPage() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const navigate = useNavigate();
  // Fetch all data — client-side search needs full dataset
  const { data, isLoading } = useNotifications(1, 9999);
  const { mutate: markRead, mutateAsync: markReadAsync, isPending: isMarking } = useMarkAsRead();
  const { mutate: markAll, isPending: isMarkingAll } = useMarkAllAsRead();

  // Bulk mark read — allSettled so partial failures are reported, not silently swallowed
  async function handleBulkMarkRead() {
    const results = await Promise.allSettled(selectedIds.map(id => markReadAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục thất bại` }));
    } else {
      appMessage.success(t('page.notifications.markReadSuccess', { defaultValue: 'Đã đánh dấu đã đọc' }));
    }
    setSelectedIds([]);
  }

  function handleRowClick(n: NotificationDto) {
    if (!n.isRead) markRead(n.id, {
      onSuccess: () => appMessage.success(t('page.notifications.markReadSuccess', { defaultValue: 'Đã đánh dấu đã đọc' })),
      onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
    });
    // L-01: Only navigate to relative paths — prevent open redirect via server-provided deepLink
    if (n.deepLink?.startsWith('/')) navigate({ to: n.deepLink });
  }

  const columns: ColumnsType<NotificationDto> = [
    {
      title: t('page.notifications.col.title'),
      key: 'title',
      render: (_: unknown, n: NotificationDto) => (
        <span style={{ fontWeight: n.isRead ? 400 : 600 }}>
          {!n.isRead && <Tag color="blue" style={{ marginRight: 8 }}>{t('page.notifications.newTag')}</Tag>}
          {n.subject}
        </span>
      ),
    },
    {
      title: t('page.notifications.col.body'),
      dataIndex: 'body',
      key: 'body',
      ellipsis: true,
      render: (v: string) => <Text type="secondary">{v}</Text>,
    },
    {
      title: t('page.notifications.col.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 150,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
    {
      title: '',
      key: 'actions',
      width: 110,
      render: (_: unknown, n: NotificationDto) =>
        !n.isRead ? (
          <Button
            size="small"
            loading={isMarking}
            onClick={(e) => { e.stopPropagation(); markRead(n.id, {
              onSuccess: () => appMessage.success(t('page.notifications.markReadSuccess', { defaultValue: 'Đã đánh dấu đã đọc' })),
              onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
            }); }}
          >
            {t('page.notifications.markRead')}
          </Button>
        ) : null,
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('page.notifications.title')}
        stats={{ total: data?.items?.length ?? 0, label: t('common.items') }}
        actions={
          <Button loading={isMarkingAll} onClick={() => markAll(undefined, {
            onSuccess: () => appMessage.success(t('page.notifications.markAllReadSuccess', { defaultValue: 'Đã đánh dấu tất cả đã đọc' })),
            onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
          })}>
            {t('page.notifications.markAllRead')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        {selectedIds.length > 0 && (
          <Flex gap={8} style={{ padding: '0 24px 8px' }}>
            <Popconfirm
              title={t('common.bulkDeleteConfirm', { defaultValue: `Đánh dấu đã đọc ${selectedIds.length} thông báo đã chọn?` })}
              onConfirm={handleBulkMarkRead}
              okText={t('common.yes', { defaultValue: 'Có' })}
              cancelText={t('common.no', { defaultValue: 'Không' })}
            >
              <Button size="small">
                {t('page.notifications.markReadSelected', { defaultValue: `Đánh dấu đã đọc (${selectedIds.length})` })}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<NotificationDto>
          rowKey="id"
          columns={columns}
          dataSource={(data?.items ?? []).filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          onRow={(n) => ({ onClick: () => handleRowClick(n), style: { cursor: 'pointer' } })}
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (n) => t('page.notifications.showTotal', { count: n }) }}
          locale={{ emptyText: t('page.notifications.empty') }}
        />
      </AdminContentCard>
    </div>
  );
}
