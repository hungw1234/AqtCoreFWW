import { useState } from 'react';
import { Table, Button, Tag, Space, Popconfirm, Tooltip, Typography, Flex } from 'antd';
import { PlusOutlined, StopOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useApiKeys, useRevokeApiKey } from './api-key-api';
import { ApiKeyCreateModal } from './api-key-create-modal';
import type { ApiKeyDto } from './api-key-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';

const { Text } = Typography;

function formatDate(iso?: string) {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('vi-VN');
}

// ApiKeyListPage — list API keys (masked), create new, revoke existing
export function ApiKeyListPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  const { data: keys = [], isFetching } = useApiKeys();
  const revokeMutation = useRevokeApiKey();

  const total = keys.length;

  // Bulk revoke — allSettled so partial failures are reported, not silently swallowed
  async function handleBulkRevoke() {
    const results = await Promise.allSettled(selectedIds.map(id => revokeMutation.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục thất bại` }));
    } else {
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
    }
    setSelectedIds([]);
  }

  const COLUMNS: ColumnsType<ApiKeyDto> = [
    {
      title: t('page.admin.apiKeys.col.name'),
      dataIndex: 'name',
      key: 'name',
      ellipsis: true,
    },
    {
      title: t('page.admin.apiKeys.col.prefix'),
      dataIndex: 'prefix',
      key: 'prefix',
      width: 180,
      render: (v: string) => (
        <Text code style={{ fontSize: 12 }}>
          {v}••••••••••••••••
        </Text>
      ),
    },
    {
      title: t('page.admin.apiKeys.col.scopes'),
      dataIndex: 'scopes',
      key: 'scopes',
      render: (scopes: string[]) => (
        <Space size={4} wrap>
          {scopes.map((s) => <Tag key={s} style={{ fontSize: 11 }}>{s}</Tag>)}
        </Space>
      ),
    },
    {
      title: t('page.admin.apiKeys.col.status'),
      dataIndex: 'isActive',
      key: 'isActive',
      width: 100,
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'red'}>
          {v ? t('page.admin.apiKeys.status.active') : t('page.admin.apiKeys.status.revoked')}
        </Tag>
      ),
    },
    {
      title: t('page.admin.apiKeys.col.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 120,
      render: (v: string) => formatDate(v),
    },
    {
      title: t('page.admin.apiKeys.col.expiresAt'),
      dataIndex: 'expiresAt',
      key: 'expiresAt',
      width: 120,
      render: (v?: string) => formatDate(v),
    },
    {
      title: t('page.admin.apiKeys.col.actions'),
      key: 'actions',
      width: 90,
      render: (_, record) =>
        record.isActive ? (
          <Popconfirm
            title={t('page.admin.apiKeys.revokeConfirm.title')}
            description={t('page.admin.apiKeys.revokeConfirm.description')}
            okText={t('page.admin.apiKeys.revokeConfirm.ok')}
            cancelText={t('common.cancel')}
            okButtonProps={{ danger: true }}
            onConfirm={() => revokeMutation.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' })),
              onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
            })}
          >
            <Tooltip title={t('page.admin.apiKeys.revokeConfirm.ok')}>
              <Button size="small" danger icon={<StopOutlined />} />
            </Tooltip>
          </Popconfirm>
        ) : null,
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('page.admin.apiKeys.title')}
        description={t('nav.apiKeysDesc')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
            {t('page.admin.apiKeys.create')}
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
              title={t('common.bulkDeleteConfirm', { defaultValue: `Thu hồi ${selectedIds.length} API key đã chọn?` })}
              onConfirm={handleBulkRevoke}
              okText={t('common.yes', { defaultValue: 'Có' })}
              cancelText={t('common.no', { defaultValue: 'Không' })}
            >
              <Button danger size="small">
                {t('common.revokeSelected', { defaultValue: `Thu hồi (${selectedIds.length})` })}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<ApiKeyDto>
          rowKey="id"
          columns={COLUMNS}
          dataSource={keys.filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isFetching}
          size="small"
          scroll={{ x: 800 }}

          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
          style={{ padding: '0 0 8px' }}
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
        />
      </AdminContentCard>

      <ApiKeyCreateModal open={modalOpen} onClose={() => setModalOpen(false)} />
    </div>
  );
}
