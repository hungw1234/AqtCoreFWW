import { useEffect } from 'react';
import { Modal, Form, Input, Switch, Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import type { IpRuleDto, CreateIpRuleRequest, UpdateIpRuleRequest } from './ip-rule-types';

// FE CIDR validation — loose structural check only. BE (FluentValidation + IPNetwork.TryParse)
// is the authoritative source of truth. FE only catches obviously malformed input early.
// Accepts IPv4 (e.g. 10.0.0.0/8) and IPv6 (e.g. 2001:db8::/32, ::1/128, ::/0).
// Host-bits-set CIDR (e.g. 192.168.1.5/24) is intentionally allowed here — BE accepts it.
const CIDR_REGEX = /^[0-9a-fA-F:.]+\/\d+$/;

interface IpRuleFormValues {
  cidr: string;
  description?: string;
  isEnabled: boolean;
  isBlock: boolean;
}

interface IpRuleFormModalProps {
  open: boolean;
  editing: IpRuleDto | null;
  saving: boolean;
  onSave: (values: CreateIpRuleRequest | UpdateIpRuleRequest) => void;
  onCancel: () => void;
}

// IpRuleFormModal — Add/Edit modal for IP filter rules (QĐ 742 §2.2.a)
// No RowVersion on IP rules — BE is last-write-wins on update.
// Self-lockout warning shown when editing an active allow-rule.
export function IpRuleFormModal({ open, editing, saving, onSave, onCancel }: IpRuleFormModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<IpRuleFormValues>();

  // Populate form when editing an existing rule
  useEffect(() => {
    if (open) {
      if (editing) {
        form.setFieldsValue({
          cidr: editing.cidr,
          description: editing.description,
          isEnabled: editing.isEnabled,
          isBlock: editing.isBlock,
        });
      } else {
        form.resetFields();
        form.setFieldsValue({ isEnabled: true, isBlock: false });
      }
    }
  }, [open, editing, form]);

  const handleOk = async () => {
    const values = await form.validateFields();
    onSave(values);
  };

  // Show self-lockout warning when the rule is a BLOCK rule and enabled.
  // A block rule covering the admin's IP/VPN range will lock them out of the system.
  // Warning fires for both create and edit scenarios when isBlock=true + isEnabled=true.
  const isBlock = Form.useWatch('isBlock', form);
  const isEnabled = Form.useWatch('isEnabled', form);
  const showAccessWarning = isBlock && isEnabled;

  return (
    <Modal
      title={editing
        ? t('ipRules.modal.editTitle', 'Sửa quy tắc IP')
        : t('ipRules.modal.createTitle', 'Thêm quy tắc IP')}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={saving}
      okText={t('common.save', 'Lưu')}
      cancelText={t('common.cancel', 'Hủy')}
      destroyOnHidden
      width={520}
    >
      {showAccessWarning && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={t('ipRules.selfLockoutWarning',
            'Cảnh báo: Quy tắc CHẶN đang bật có thể khóa IP của bạn. Hãy kiểm tra địa chỉ IP công cộng trước khi lưu.')}
        />
      )}

      <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
        <Form.Item
          name="cidr"
          label={t('ipRules.form.cidr', 'CIDR (IPv4 hoặc IPv6)')}
          rules={[
            { required: true, message: t('ipRules.form.cidrRequired', 'Vui lòng nhập CIDR') },
            {
              pattern: CIDR_REGEX,
              message: t('ipRules.form.cidrInvalid', 'CIDR không hợp lệ. Ví dụ: 192.168.1.0/24'),
            },
          ]}
          tooltip={t('ipRules.form.cidrTooltip', 'Ví dụ: 192.168.1.0/24 hoặc 10.0.0.5/32')}
        >
          <Input placeholder="192.168.1.0/24" />
        </Form.Item>

        <Form.Item
          name="description"
          label={t('ipRules.form.description', 'Mô tả')}
          rules={[{ max: 500, message: t('ipRules.form.descriptionMaxLen', 'Tối đa 500 ký tự') }]}
        >
          <Input.TextArea rows={3} placeholder={t('ipRules.form.descriptionPlaceholder', 'Mô tả mục đích của quy tắc...')} />
        </Form.Item>

        <Form.Item
          name="isBlock"
          label={t('ipRules.form.isBlock', 'Loại quy tắc')}
          valuePropName="checked"
          tooltip={t('ipRules.form.isBlockTooltip', 'Bật: chặn IP. Tắt: cho phép IP (whitelist).')}
        >
          <Switch
            checkedChildren={t('ipRules.form.block', 'Chặn')}
            unCheckedChildren={t('ipRules.form.allow', 'Cho phép')}
          />
        </Form.Item>

        <Form.Item
          name="isEnabled"
          label={t('ipRules.form.isEnabled', 'Kích hoạt')}
          valuePropName="checked"
        >
          <Switch />
        </Form.Item>
      </Form>
    </Modal>
  );
}
