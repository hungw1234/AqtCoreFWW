import { useEffect } from 'react';
import { Modal, Form, Input, InputNumber, Switch, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import type {
  AuditRetentionPolicyDto,
  CreateAuditRetentionPolicyRequest,
  UpdateAuditRetentionPolicyRequest,
  AuditCategory,
} from './audit-policy-api';

// Display labels for AuditCategory enum values (QĐ 742 mục 3.1.b — 5 nhóm bắt buộc)
const CATEGORY_LABELS: Record<AuditCategory, string> = {
  AppAccess: '(i) Truy cập phần mềm',
  AdminLogin: '(ii) Đăng nhập quản trị',
  Error: '(iii) Lỗi phát sinh',
  AccountManagement: '(iv) Quản lý tài khoản',
  ConfigChange: '(v) Thay đổi cấu hình hệ thống',
};

const CATEGORY_OPTIONS = (Object.keys(CATEGORY_LABELS) as AuditCategory[]).map((k) => ({
  label: CATEGORY_LABELS[k],
  value: k,
}));

interface FormValues {
  category?: AuditCategory;
  retentionDays: number;
  archiveBeforePurge: boolean;
  archivePath?: string;
}

interface AuditPolicyFormModalProps {
  open: boolean;
  editing: AuditRetentionPolicyDto | null;
  saving: boolean;
  onSave: (values: CreateAuditRetentionPolicyRequest | UpdateAuditRetentionPolicyRequest) => void;
  onCancel: () => void;
}

// AuditPolicyFormModal — create or edit an audit retention policy row (QĐ 742 §3.2.a/b)
export function AuditPolicyFormModal({
  open,
  editing,
  saving,
  onSave,
  onCancel,
}: AuditPolicyFormModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<FormValues>();

  useEffect(() => {
    if (open) {
      if (editing) {
        form.setFieldsValue({
          category: editing.category,
          retentionDays: editing.retentionDays,
          archiveBeforePurge: editing.archiveBeforePurge,
          archivePath: editing.archivePath,
        });
      } else {
        form.resetFields();
        form.setFieldsValue({ retentionDays: 365, archiveBeforePurge: false });
      }
    }
  }, [open, editing, form]);

  const handleOk = async () => {
    const values = await form.validateFields();
    if (editing) {
      // PUT requires rowVersion for optimistic concurrency
      onSave({ ...values, rowVersion: editing.rowVersion } as UpdateAuditRetentionPolicyRequest);
    } else {
      onSave(values as CreateAuditRetentionPolicyRequest);
    }
  };

  return (
    <Modal
      title={
        editing
          ? t('auditPolicy.modal.editTitle', 'Sửa chính sách lưu trữ nhật ký')
          : t('auditPolicy.modal.createTitle', 'Thêm chính sách lưu trữ nhật ký')
      }
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={saving}
      okText={t('common.save', 'Lưu')}
      cancelText={t('common.cancel', 'Hủy')}
      destroyOnHidden
      width={520}
    >
      <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
        {/* Category only shown on create — editing a row doesn't change its category */}
        {!editing && (
          <Form.Item
            name="category"
            label={t('auditPolicy.form.category', 'Nhóm nhật ký (QĐ 742 §3.1.b)')}
            tooltip={t('auditPolicy.form.categoryTooltip', 'Để trống = quy tắc mặc định (global fallback cho mọi nhóm).')}
          >
            <Select
              allowClear
              placeholder={t('auditPolicy.form.categoryPlaceholder', 'Để trống = mặc định toàn cục')}
              options={CATEGORY_OPTIONS}
            />
          </Form.Item>
        )}

        {editing && editing.category && (
          <Form.Item label={t('auditPolicy.form.category', 'Nhóm nhật ký')}>
            <span style={{ fontWeight: 500 }}>{CATEGORY_LABELS[editing.category]}</span>
          </Form.Item>
        )}

        <Form.Item
          name="retentionDays"
          label={t('auditPolicy.form.retentionDays', 'Số ngày lưu trữ')}
          rules={[
            { required: true, message: t('auditPolicy.form.retentionDaysRequired', 'Vui lòng nhập số ngày') },
            { type: 'number', min: 1, max: 36500, message: t('auditPolicy.form.retentionDaysRange', 'Từ 1 đến 36500 ngày') },
          ]}
          tooltip={t('auditPolicy.form.retentionDaysTooltip', 'QĐ 742 §3.2.a yêu cầu tối thiểu 365 ngày (1 năm).')}
        >
          <InputNumber min={1} max={36500} style={{ width: '100%' }} addonAfter="ngày" />
        </Form.Item>

        <Form.Item
          name="archiveBeforePurge"
          label={t('auditPolicy.form.archiveBeforePurge', 'Lưu trữ trước khi xóa')}
          valuePropName="checked"
          tooltip={t('auditPolicy.form.archiveTooltip', 'Nếu bật, dữ liệu được copy sang đường dẫn lưu trữ trước khi xóa.')}
        >
          <Switch />
        </Form.Item>

        <Form.Item
          noStyle
          shouldUpdate={(prev, cur) => prev.archiveBeforePurge !== cur.archiveBeforePurge}
        >
          {({ getFieldValue }) =>
            getFieldValue('archiveBeforePurge') ? (
              <Form.Item
                name="archivePath"
                label={t('auditPolicy.form.archivePath', 'Đường dẫn lưu trữ')}
                rules={[
                  { required: true, message: t('auditPolicy.form.archivePathRequired', 'Vui lòng nhập đường dẫn lưu trữ') },
                  { max: 1000, message: t('auditPolicy.form.archivePathMaxLen', 'Tối đa 1000 ký tự') },
                ]}
              >
                <Input placeholder="/archive/audit-logs or s3://bucket/audit" />
              </Form.Item>
            ) : null
          }
        </Form.Item>
      </Form>
    </Modal>
  );
}
