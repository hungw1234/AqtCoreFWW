import { useState } from 'react';
import { Table, Button, Alert } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useAuditRetentionPolicies,
  useCreateAuditRetentionPolicy,
  useUpdateAuditRetentionPolicy,
  useDeleteAuditRetentionPolicy,
  type AuditRetentionPolicyDto,
  type CreateAuditRetentionPolicyRequest,
  type UpdateAuditRetentionPolicyRequest,
} from './audit-policy-api';
import { AuditPolicyFormModal } from './audit-policy-form-modal';
import { useAuditPolicyColumns } from './use-audit-policy-columns';

// AuditPolicyPage — manage audit log retention policies per category (QĐ 742 §3.2.a/b)
// Route: GET /api/v1/audit/retention-policies (audit module route, not /admin/)
// No "Last Purge Run" field — purge schedule is BE-internal, not exposed in DTO.
export function AuditPolicyPage() {
  const { t } = useTranslation();
  const [searchText, setSearchText] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editingPolicy, setEditingPolicy] = useState<AuditRetentionPolicyDto | null>(null);

  const { data: policies = [], isLoading } = useAuditRetentionPolicies();
  const createPolicy = useCreateAuditRetentionPolicy();
  const updatePolicy = useUpdateAuditRetentionPolicy();
  const deletePolicy = useDeleteAuditRetentionPolicy();

  const isSaving = createPolicy.isPending || updatePolicy.isPending;

  function openCreate() { setEditingPolicy(null); setModalOpen(true); }
  function openEdit(p: AuditRetentionPolicyDto) { setEditingPolicy(p); setModalOpen(true); }
  function closeModal() { setModalOpen(false); setEditingPolicy(null); }

  async function handleSave(
    values: CreateAuditRetentionPolicyRequest | UpdateAuditRetentionPolicyRequest,
  ) {
    try {
      if (editingPolicy) {
        await updatePolicy.mutateAsync({ id: editingPolicy.id, ...(values as UpdateAuditRetentionPolicyRequest) });
      } else {
        await createPolicy.mutateAsync(values as CreateAuditRetentionPolicyRequest);
      }
      appMessage.success(t('common.operationSuccess', 'Thao tác thành công'));
      closeModal();
    } catch {
      appMessage.error(t('common.operationFailed', 'Thao tác thất bại'));
    }
  }

  async function handleDelete(record: AuditRetentionPolicyDto) {
    try {
      await deletePolicy.mutateAsync({ id: record.id, rowVersion: record.rowVersion });
      appMessage.success(t('common.deleteSuccess', 'Xóa thành công'));
    } catch {
      appMessage.error(t('common.deleteFailed', 'Xóa thất bại'));
    }
  }

  const columns = useAuditPolicyColumns({
    onEdit: openEdit,
    onDelete: handleDelete,
    isDeleting: deletePolicy.isPending,
  });

  const filtered = policies.filter(
    (p) =>
      !searchText ||
      [p.category ?? '', p.archivePath ?? ''].some((v) =>
        v.toLowerCase().includes(searchText.toLowerCase()),
      ),
  );

  const belowMinCount = policies.filter((p) => p.retentionDays < 365).length;

  return (
    <div>
      <AdminPageHeader
        title={t('auditPolicy.title', 'Chính sách lưu trữ nhật ký kiểm toán (QĐ 742 §3.2.a/b)')}
        description={t('auditPolicy.description', 'Quy định số ngày lưu trữ nhật ký theo từng nhóm sự kiện. QĐ 742 yêu cầu tối thiểu 365 ngày.')}
        stats={{ total: policies.length, label: t('auditPolicy.policyLabel', 'chính sách') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('common.add', 'Thêm')}
          </Button>
        }
      />

      {belowMinCount > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={t('auditPolicy.belowMinAlert', `${belowMinCount} chính sách có thời gian lưu trữ dưới 365 ngày — không đáp ứng QĐ 742 §3.2.a.`)}
        />
      )}

      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('auditPolicy.searchPlaceholder', 'Tìm theo nhóm hoặc đường dẫn...')}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        <Table<AuditRetentionPolicyDto>
          rowKey="id"
          columns={columns}
          dataSource={filtered}
          loading={isLoading}
          size="small"
          scroll={{ x: 900 }}
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (n) => `${t('common.total', 'Tổng')} ${n}` }}
          locale={{ emptyText: t('auditPolicy.noPolicies', 'Chưa có chính sách lưu trữ nào') }}
        />
      </AdminContentCard>

      <AuditPolicyFormModal
        open={modalOpen}
        editing={editingPolicy}
        saving={isSaving}
        onSave={handleSave}
        onCancel={closeModal}
      />
    </div>
  );
}
