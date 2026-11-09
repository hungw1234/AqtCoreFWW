import { useState } from 'react';
import { Table, Button, Space, Popconfirm, Tag, Modal, Form, Input, Switch, DatePicker, Flex } from 'antd';
import { PlusOutlined, StopOutlined, CheckOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import { usePermissions } from '@/features/auth/use-permissions';
import { useMyEffectivePermissions } from '@/features/auth/me-permissions-api';
import {
  useDelegations,
  useCreateDelegation,
  useRevokeDelegation,
  useApproveDelegation,
  type DelegationDto,
  type DelegationStatus,
  type CreateDelegationDto,
} from './delegation-api';

// Per-status badge color + i18n label. The delegate inherits roles only while Active; PendingApproval
// (created on behalf of another) needs the delegator's / a SystemAdmin's approval before it takes effect.
const STATUS_META: Record<DelegationStatus, { color: string; labelKey: string; fallback: string }> = {
  Active: { color: 'green', labelKey: 'delegations.statusActive', fallback: 'Hiệu lực' },
  PendingApproval: { color: 'gold', labelKey: 'delegations.statusPending', fallback: 'Chờ duyệt' },
  Expired: { color: 'default', labelKey: 'delegations.statusExpired', fallback: 'Hết hạn' },
  Revoked: { color: 'red', labelKey: 'delegations.statusRevoked', fallback: 'Đã thu hồi' },
};
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';

const { RangePicker } = DatePicker;

// Delegation management page — list active delegations, create new, revoke existing
export function DelegationListPage() {
  const { t } = useTranslation();
  const [activeOnly, setActiveOnly] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [form] = Form.useForm();

  const { data, isLoading } = useDelegations(activeOnly);
  const createDelegation = useCreateDelegation();
  const revokeDelegation = useRevokeDelegation();
  const approveDelegation = useApproveDelegation();

  // Eligibility for the approve action mirrors the BE authority (delegator or SystemAdmin); the BE
  // re-enforces it, so this only governs whether the button is shown — no security relies on the FE gate.
  const { hasRole } = usePermissions();
  const { data: me } = useMyEffectivePermissions();
  const myUserId = me?.userId;

  // BE returns PagedResult<T> = { items, totalCount, meta } — extract items array.
  // Guard with Array.isArray to survive legacy/future shape drift (see feedback-fe-dto-must-match-be-controller).
  const delegations = Array.isArray(data?.items) ? data.items : [];
  const total = data?.totalCount ?? delegations.length;

  // Bulk revoke — allSettled so partial failures are reported, not silently swallowed
  async function handleBulkRevoke() {
    const results = await Promise.allSettled(selectedIds.map(id => revokeDelegation.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục thất bại` }));
    } else {
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
    }
    setSelectedIds([]);
  }

  // Names null khi user not found / cross-tenant — render placeholder thay vì GUID raw để rõ data state
  const deletedUserLabel = t('delegations.deletedUser', { defaultValue: '(Đã xoá)' });

  const columns: ColumnsType<DelegationDto> = [
    { title: t('delegations.col.delegator'), dataIndex: 'delegatorName', key: 'delegatorName', width: 160, ellipsis: true, render: (v: string | null | undefined) => v ?? deletedUserLabel },
    { title: t('delegations.col.delegate'), dataIndex: 'delegateName', key: 'delegateName', width: 160, ellipsis: true, render: (v: string | null | undefined) => v ?? deletedUserLabel },
    { title: t('delegations.col.validFrom'), dataIndex: 'validFrom', key: 'validFrom', width: 130, render: (v: string) => dayjs(v).format('DD/MM/YYYY') },
    { title: t('delegations.col.validTo'), dataIndex: 'validTo', key: 'validTo', width: 130, render: (v: string) => dayjs(v).format('DD/MM/YYYY') },
    { title: t('delegations.col.reason'), dataIndex: 'reason', key: 'reason', ellipsis: true, render: (v) => v ?? '—' },
    {
      title: t('delegations.col.status'),
      key: 'status',
      width: 110,
      // BE returns the authoritative lifecycle status (Active / PendingApproval / Expired / Revoked).
      render: (_, record) => {
        const meta = STATUS_META[record.status] ?? { color: 'default', labelKey: '', fallback: record.status };
        return <Tag color={meta.color}>{meta.labelKey ? t(meta.labelKey, { defaultValue: meta.fallback }) : meta.fallback}</Tag>;
      },
    },
    {
      title: t('common.actions', { defaultValue: 'Thao tác' }),
      key: 'actions',
      width: 150,
      render: (_, record) => {
        // Show approve only on pending rows the current user may approve (delegator or SystemAdmin).
        const canApprove = record.status === 'PendingApproval'
          && (record.delegatorId === myUserId || hasRole('SystemAdmin'));
        return (
          <Space size={4}>
            {canApprove && (
              <Popconfirm
                title={t('delegations.approveConfirm', { defaultValue: 'Duyệt ủy quyền này?' })}
                onConfirm={() => approveDelegation.mutate(record.id, {
                  onSuccess: () => appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' })),
                  onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
                })}
                okText={t('common.confirm')}
                cancelText={t('common.cancel')}
              >
                <Button type="primary" icon={<CheckOutlined />} size="small" loading={approveDelegation.isPending}>
                  {t('delegations.approve', { defaultValue: 'Duyệt' })}
                </Button>
              </Popconfirm>
            )}
            <Popconfirm
              title={t('delegations.revokeConfirm')}
              onConfirm={() => revokeDelegation.mutate(record.id, {
                onSuccess: () => appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' })),
                onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
              })}
              okText={t('common.confirm')}
              cancelText={t('common.cancel')}
            >
              <Button danger icon={<StopOutlined />} size="small" loading={revokeDelegation.isPending} />
            </Popconfirm>
          </Space>
        );
      },
    },
  ];

  const handleCreate = async () => {
    const values = await form.validateFields();
    const [validFrom, validTo] = values.dateRange;
    try {
      await createDelegation.mutateAsync({
        delegatorId: values.delegatorId,
        delegateId: values.delegateId,
        validFrom: validFrom.toISOString(),
        validTo: validTo.toISOString(),
        reason: values.reason,
      } as CreateDelegationDto);
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
      form.resetFields();
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }));
    }
  };

  return (
    <div>
      <AdminPageHeader
        title={t('delegations.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Space>
            <Switch
              checked={activeOnly}
              onChange={setActiveOnly}
              checkedChildren={t('delegations.activeOnly')}
              unCheckedChildren={t('delegations.all')}
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
              {t('common.add')}
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
        {selectedIds.length > 0 && (
          <Flex gap={8} style={{ padding: '0 24px 8px' }}>
            <Popconfirm
              title={t('common.bulkDeleteConfirm', { defaultValue: `Thu hồi ${selectedIds.length} ủy quyền đã chọn?` })}
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
        <Table<DelegationDto>
          rowKey="id"
          columns={columns}
          dataSource={delegations.filter(item =>
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

      <Modal
        title={t('delegations.createTitle')}
        open={modalOpen}
        onOk={handleCreate}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        confirmLoading={createDelegation.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="delegatorId" label={t('delegations.col.delegator')} rules={[{ required: true }]}>
            <Input placeholder={t('delegations.userIdPlaceholder')} />
          </Form.Item>
          <Form.Item name="delegateId" label={t('delegations.col.delegate')} rules={[{ required: true }]}>
            <Input placeholder={t('delegations.userIdPlaceholder')} />
          </Form.Item>
          <Form.Item name="dateRange" label={t('delegations.validRange')} rules={[{ required: true }]}>
            <RangePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="reason" label={t('delegations.col.reason')}>
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
