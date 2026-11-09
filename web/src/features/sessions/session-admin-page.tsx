import { useState } from 'react';
import { Table, Button, Space, Popconfirm, Tooltip, Flex } from 'antd';
import { StopOutlined, UserDeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useActiveSessions,
  useRevokeSession,
  useRevokeUserSessions,
  type ActiveSessionDto,
} from './session-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { AdminContentCard } from '@/shared/components/admin-content-card';

// Active session management admin page — view, filter, and revoke user sessions (server-paginated)
export function SessionAdminPage() {
  const { t } = useTranslation();
  const [searchInput, setSearchInput] = useState('');
  const [userId, setUserId] = useState('');
  const [selectedTokenIds, setSelectedTokenIds] = useState<string[]>([]);

  const { antPagination, toQueryParams } = useServerPagination(20, [userId]);
  const { pageNumber, pageSize } = toQueryParams();

  const { data, isFetching } = useActiveSessions({
    userId: userId || undefined,
    page: pageNumber,
    pageSize,
  });

  const revokeSession = useRevokeSession();
  const revokeUserSessions = useRevokeUserSessions();

  const sessions = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  const columns: ColumnsType<ActiveSessionDto> = [
    {
      title: t('sessions.col.user'),
      dataIndex: 'userEmail',
      key: 'userEmail',
      width: 200,
      ellipsis: true,
      render: (v: string | undefined, r) => v ?? r.userId?.slice(0, 12) + '…',
    },
    {
      title: 'Ứng dụng',
      dataIndex: 'clientId',
      key: 'clientId',
      ellipsis: true,
      render: (v) => v ?? '—',
    },
    { title: t('sessions.col.createdAt'), dataIndex: 'issuedAt', key: 'issuedAt', width: 150, render: (v: string) => v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—' },
    { title: t('sessions.col.expiresAt'), dataIndex: 'expiresAt', key: 'expiresAt', width: 140, render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm') },
    {
      title: t('sessions.col.actions'),
      key: 'actions',
      width: 120,
      render: (_, record) => (
        <Space>
          <Popconfirm
            title={t('sessions.revokeConfirm')}
            onConfirm={() => revokeSession.mutate(record.tokenId, {
              onSuccess: () => appMessage.success(t('sessions.revokeSuccess', 'Phiên đã được thu hồi')),
              onError: () => appMessage.error(t('sessions.revokeError', 'Thu hồi phiên thất bại')),
            })}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
          >
            <Tooltip title={t('sessions.revoke')}>
              <Button danger icon={<StopOutlined />} size="small" loading={revokeSession.isPending} />
            </Tooltip>
          </Popconfirm>
          <Popconfirm
            title={t('sessions.revokeAllConfirm')}
            onConfirm={() => revokeUserSessions.mutate(record.userId, {
              onSuccess: () => appMessage.success(t('sessions.revokeAllSuccess', 'Tất cả phiên đã được thu hồi')),
              onError: () => appMessage.error(t('sessions.revokeAllError', 'Thu hồi phiên thất bại')),
            })}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
          >
            <Tooltip title={t('sessions.revokeAll')}>
              <Button icon={<UserDeleteOutlined />} size="small" loading={revokeUserSessions.isPending} />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  // Bulk revoke — allSettled so partial failures are reported, not silently swallowed
  async function handleBulkRevoke() {
    const results = await Promise.allSettled(selectedTokenIds.map(tokenId => revokeSession.mutateAsync(tokenId)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedTokenIds.length, defaultValue: `${failed}/${selectedTokenIds.length} phiên thu hồi thất bại` }));
    } else {
      appMessage.success(t('sessions.revokeSuccess', 'Phiên đã được thu hồi'));
    }
    setSelectedTokenIds([]);
  }

  // Trigger search on Enter or clear
  function handleSearch(value: string) {
    setUserId(value.trim());
  }

  return (
    <div>
      <AdminPageHeader
        title={t('sessions.title')}
        stats={{ total, label: t('common.items') }}
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('sessions.filterPlaceholder')}
          searchValue={searchInput}
          onSearchChange={(v) => {
            setSearchInput(v);
            // Clear server filter when input cleared
            if (!v) handleSearch('');
          }}
          actions={
            <Button onClick={() => handleSearch(searchInput)}>
              {t('common.search', 'Tìm')}
            </Button>
          }
        />
        {selectedTokenIds.length > 0 && (
          <Flex gap={8} style={{ padding: '0 24px 8px' }}>
            <Popconfirm
              title={t('sessions.revokeConfirm', `Thu hồi ${selectedTokenIds.length} phiên đã chọn?`)}
              onConfirm={handleBulkRevoke}
              okText={t('common.confirm')}
              cancelText={t('common.cancel')}
            >
              <Button danger size="small">
                {t('common.revokeSelected', `Thu hồi (${selectedTokenIds.length})`)}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<ActiveSessionDto>
          rowKey="tokenId"
          columns={columns}
          dataSource={sessions}
          loading={isFetching}
          size="small"
          pagination={{ ...antPagination, total }}
          locale={{ emptyText: t('sessions.noSessions') }}
          rowSelection={{
            selectedRowKeys: selectedTokenIds,
            onChange: (keys) => setSelectedTokenIds(keys as string[]),
          }}
        />
      </AdminContentCard>
    </div>
  );
}
