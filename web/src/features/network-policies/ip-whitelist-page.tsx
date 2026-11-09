import { useState } from 'react';
import { Table, Button, Space, Popconfirm, Tag } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { useIpRules, useCreateIpRule, useUpdateIpRule, useDeleteIpRule } from './ip-rule-api';
import { IpRuleFormModal } from './ip-rule-form-modal';
import type { IpRuleDto, CreateIpRuleRequest, UpdateIpRuleRequest } from './ip-rule-types';
import { appMessage } from '@/core/api/notification-bridge';

// IpWhitelistPage — admin CRUD for IP filter rules (QĐ 742 §2.2.a)
// Rules are evaluated by IpFilterMiddleware (cache TTL 5min, DB fallback on miss).
export function IpWhitelistPage() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editingRule, setEditingRule] = useState<IpRuleDto | null>(null);

  const { data: rules = [], isLoading } = useIpRules();
  const createRule = useCreateIpRule();
  const updateRule = useUpdateIpRule();
  const deleteRule = useDeleteIpRule();

  const isSaving = createRule.isPending || updateRule.isPending;

  function openCreate() {
    setEditingRule(null);
    setModalOpen(true);
  }

  function openEdit(rule: IpRuleDto) {
    setEditingRule(rule);
    setModalOpen(true);
  }

  function closeModal() {
    setModalOpen(false);
    setEditingRule(null);
  }

  async function handleSave(values: CreateIpRuleRequest | UpdateIpRuleRequest) {
    try {
      if (editingRule) {
        await updateRule.mutateAsync({ id: editingRule.id, ...values as UpdateIpRuleRequest });
      } else {
        await createRule.mutateAsync(values as CreateIpRuleRequest);
      }
      appMessage.success(t('common.operationSuccess', 'Thao tác thành công'));
      closeModal();
    } catch {
      appMessage.error(t('common.operationFailed', 'Thao tác thất bại'));
    }
  }

  async function handleDelete(id: number) {
    try {
      await deleteRule.mutateAsync(id);
      appMessage.success(t('common.deleteSuccess', 'Xóa thành công'));
    } catch {
      appMessage.error(t('common.deleteFailed', 'Xóa thất bại'));
    }
  }

  const filtered = rules.filter(
    (r) =>
      !searchText ||
      [r.cidr, r.description].some((v) =>
        (v ?? '').toLowerCase().includes(searchText.toLowerCase()),
      ),
  );

  const columns: ColumnsType<IpRuleDto> = [
    {
      title: t('ipRules.col.cidr', 'CIDR'),
      dataIndex: 'cidr',
      key: 'cidr',
      width: 200,
      render: (v: string) => <code style={{ fontSize: 13 }}>{v}</code>,
    },
    {
      title: t('ipRules.col.type', 'Loại'),
      dataIndex: 'isBlock',
      key: 'isBlock',
      width: 100,
      render: (v: boolean) =>
        v
          ? <Tag color="red">{t('ipRules.block', 'Chặn')}</Tag>
          : <Tag color="green">{t('ipRules.allow', 'Cho phép')}</Tag>,
    },
    {
      title: t('ipRules.col.description', 'Mô tả'),
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
    },
    {
      title: t('ipRules.col.status', 'Trạng thái'),
      dataIndex: 'isEnabled',
      key: 'isEnabled',
      width: 110,
      render: (v: boolean) =>
        v
          ? <Tag color="success">{t('common.active', 'Hoạt động')}</Tag>
          : <Tag>{t('common.inactive', 'Tắt')}</Tag>,
    },
    {
      title: t('ipRules.col.updatedAt', 'Cập nhật'),
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 150,
      render: (v?: string) => v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—',
    },
    {
      title: '',
      key: 'actions',
      width: 90,
      render: (_, record) => (
        <Space size="small">
          <Button
            size="small"
            icon={<EditOutlined />}
            onClick={() => openEdit(record)}
          />
          <Popconfirm
            title={t('ipRules.deleteConfirm', 'Xóa quy tắc IP này?')}
            onConfirm={() => handleDelete(record.id)}
            okText={t('common.confirm', 'Xác nhận')}
            cancelText={t('common.cancel', 'Hủy')}
          >
            <Button
              danger
              size="small"
              icon={<DeleteOutlined />}
              loading={deleteRule.isPending}
            />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('ipRules.title', 'Quản lý quy tắc IP (QĐ 742 §2.2.a)')}
        description={t('ipRules.description', 'Danh sách quy tắc cho phép/chặn IP quản trị từ xa. Thay đổi áp dụng sau ≤60s (cache TTL).')}
        stats={{ total: rules.length, filtered: filtered.length !== rules.length ? filtered.length : undefined, label: t('ipRules.ruleLabel', 'quy tắc') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('common.add', 'Thêm')}
          </Button>
        }
      />

      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('ipRules.searchPlaceholder', 'Tìm theo CIDR hoặc mô tả...')}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        <Table<IpRuleDto>
          rowKey="id"
          columns={columns}
          dataSource={filtered}
          loading={isLoading}
          size="small"
          scroll={{ x: 700 }}
          pagination={{
            pageSize: 20,
            showSizeChanger: true,
            showTotal: (total) => `${t('common.total', 'Tổng')} ${total}`,
          }}
          locale={{ emptyText: t('ipRules.noRules', 'Chưa có quy tắc IP nào') }}
        />
      </AdminContentCard>

      <IpRuleFormModal
        open={modalOpen}
        editing={editingRule}
        saving={isSaving}
        onSave={handleSave}
        onCancel={closeModal}
      />
    </div>
  );
}
