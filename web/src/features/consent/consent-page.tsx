// Consent management page — user-facing PDPL consent list with withdraw action

import { useState } from 'react';
import { Table, Button, Tag, Popconfirm, Flex, Tooltip } from 'antd';
import { MinusCircleOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useConsents, useWithdrawConsent, type ConsentRecordDto } from './consent-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';

export function ConsentPage() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const { data, isLoading } = useConsents();
  const withdrawMutation = useWithdrawConsent();

  // Bulk withdraw — allSettled so partial failures are reported
  async function handleBulkWithdraw() {
    const records = data ?? [];
    const toWithdraw = records.filter(r => selectedIds.includes(r.id) && !r.isWithdrawn);
    const results = await Promise.allSettled(toWithdraw.map(r => withdrawMutation.mutateAsync({ purpose: r.purpose })));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: toWithdraw.length, defaultValue: `${failed}/${toWithdraw.length} thao tác thất bại` }));
    } else {
      appMessage.success(t('consent.withdrawSuccess', 'Đã rút đồng ý thành công'));
    }
    setSelectedIds([]);
  }

  const handleWithdraw = async (record: ConsentRecordDto) => {
    try {
      await withdrawMutation.mutateAsync({ purpose: record.purpose });
      appMessage.success(t('consent.withdrawSuccess', 'Đã rút đồng ý thành công'));
    } catch {
      appMessage.error(t('common.error', 'Có lỗi xảy ra'));
    }
  };

  const columns: ColumnsType<ConsentRecordDto> = [
    {
      title: t('consent.col.purpose', 'Mục đích'),
      dataIndex: 'purpose',
      key: 'purpose',
      ellipsis: true,
    },
    {
      title: t('consent.col.legalBasis', 'Cơ sở pháp lý'),
      dataIndex: 'legalBasis',
      key: 'legalBasis',
      width: 180,
      ellipsis: true,
    },
    {
      title: t('consent.col.createdAt', 'Ngày tạo'),
      dataIndex: 'createdAtUtc',
      key: 'createdAtUtc',
      width: 160,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
    {
      title: t('consent.col.status', 'Trạng thái'),
      key: 'status',
      width: 120,
      render: (_: unknown, record: ConsentRecordDto) =>
        record.isWithdrawn ? (
          <Tag color="red">{t('consent.status.withdrawn', 'Đã rút')}</Tag>
        ) : (
          <Tag color="green">{t('consent.status.active', 'Hoạt động')}</Tag>
        ),
    },
    {
      title: '',
      key: 'actions',
      width: 120,
      render: (_: unknown, record: ConsentRecordDto) =>
        record.isWithdrawn ? null : (
          <Popconfirm
            title={t('consent.withdrawConfirm', 'Bạn có chắc muốn rút đồng ý này?')}
            onConfirm={() => handleWithdraw(record)}
            okText={t('common.confirm', 'Xác nhận')}
            cancelText={t('common.cancel', 'Hủy')}
            okButtonProps={{ danger: true }}
          >
            <Tooltip title={t('consent.withdraw', 'Rút đồng ý')}>
              <Button danger size="small" icon={<MinusCircleOutlined />} loading={withdrawMutation.isPending} />
            </Tooltip>
          </Popconfirm>
        ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('consent.title', 'Quản lý đồng ý xử lý dữ liệu')}
        description={t('consent.withdrawWarning', 'Rút đồng ý có thể ảnh hưởng đến quyền truy cập dữ liệu của bạn')}
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
              title={t('consent.withdrawConfirm', `Rút đồng ý ${selectedIds.length} mục đã chọn?`)}
              onConfirm={handleBulkWithdraw}
              okText={t('common.confirm', 'Xác nhận')}
              cancelText={t('common.cancel', 'Hủy')}
              okButtonProps={{ danger: true }}
            >
              <Button danger size="small">
                {t('consent.withdrawSelected', `Rút đồng ý (${selectedIds.length})`)}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<ConsentRecordDto>
          rowKey="id"
          columns={columns}
          dataSource={(data ?? []).filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
        />
      </AdminContentCard>
    </div>
  );
}
