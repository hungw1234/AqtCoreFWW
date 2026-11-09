import { useState } from 'react';
import { Table, Button, Space, Popconfirm, Tag, Flex } from 'antd';
import { CheckOutlined, CloseOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import {
  usePendingAccessReviews,
  useApproveAccessReview,
  useRejectAccessReview,
  type AccessReviewDto,
} from './access-review-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';

// Access review admin page — list pending access requests with approve / reject actions
export function AccessReviewPage() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  // Fetch all data — client-side search needs full dataset
  const { data, isLoading } = usePendingAccessReviews(1, 9999);
  const approve = useApproveAccessReview();
  const reject = useRejectAccessReview();

  async function handleBulkApprove() {
    const results = await Promise.allSettled(selectedIds.map((id) => approve.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục thất bại` }));
    } else {
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
    }
    setSelectedIds([]);
  }

  async function handleBulkReject() {
    const results = await Promise.allSettled(selectedIds.map((id) => reject.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục thất bại` }));
    } else {
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
    }
    setSelectedIds([]);
  }

  const columns: ColumnsType<AccessReviewDto> = [
    {
      title: t('accessReviews.col.user'),
      dataIndex: 'userName',
      key: 'userName',
      width: 160,
      ellipsis: true,
      render: (v, r) => v ?? r.userId,
    },
    { title: t('accessReviews.col.resourceType'), dataIndex: 'resourceType', key: 'resourceType', width: 130, ellipsis: true },
    { title: t('accessReviews.col.resourceId'), dataIndex: 'resourceId', key: 'resourceId', width: 140, ellipsis: true },
    { title: t('accessReviews.col.permission'), dataIndex: 'permission', key: 'permission', width: 150, ellipsis: true },
    // no width — flex fills remaining space; requestedBy is a name that can vary in length
    { title: t('accessReviews.col.requestedBy'), dataIndex: 'requestedBy', key: 'requestedBy', ellipsis: true },
    {
      title: t('accessReviews.col.requestedAt'),
      dataIndex: 'requestedAt',
      key: 'requestedAt',
      width: 140,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
    {
      title: t('accessReviews.col.status'),
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (v: string) => <Tag color="orange">{v}</Tag>,
    },
    {
      title: t('accessReviews.col.actions'),
      key: 'actions',
      width: 120,
      render: (_, record) => (
        <Space>
          <Popconfirm
            title={t('accessReviews.approveConfirm')}
            onConfirm={() => approve.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' })),
              onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
            })}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
          >
            <Button type="primary" icon={<CheckOutlined />} size="small" loading={approve.isPending} />
          </Popconfirm>
          <Popconfirm
            title={t('accessReviews.rejectConfirm')}
            onConfirm={() => reject.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' })),
              onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
            })}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
          >
            <Button danger icon={<CloseOutlined />} size="small" loading={reject.isPending} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('accessReviews.title')}
        stats={{ total: data?.items?.length ?? 0, label: t('common.items') }}
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
              title={t('common.bulkApproveConfirm', { defaultValue: `Duyệt ${selectedIds.length} mục đã chọn?` })}
              onConfirm={handleBulkApprove}
            >
              <Button type="primary" size="small">
                {t('accessReviews.approveAll', { defaultValue: `Duyệt tất cả (${selectedIds.length})` })}
              </Button>
            </Popconfirm>
            <Popconfirm
              title={t('common.bulkRejectConfirm', { defaultValue: `Từ chối ${selectedIds.length} mục đã chọn?` })}
              onConfirm={handleBulkReject}
            >
              <Button danger size="small">
                {t('accessReviews.rejectAll', { defaultValue: `Từ chối tất cả (${selectedIds.length})` })}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<AccessReviewDto>
          rowKey="id"
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          columns={columns}
          dataSource={(data?.items ?? []).filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          scroll={{ x: 900 }}
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
          locale={{ emptyText: t('accessReviews.noPending') }}
        />
      </AdminContentCard>
    </div>
  );
}
