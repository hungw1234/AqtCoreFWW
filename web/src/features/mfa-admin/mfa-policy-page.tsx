import { useState } from 'react';
import {
  Table,
  Button,
  Space,
  Tag,
  Modal,
  Form,
  InputNumber,
  Switch,
  Checkbox,
  Divider,
  Tooltip,
} from 'antd';
import { EditOutlined, InfoCircleOutlined, PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useMfaPolicies, useCreateMfaPolicy, useUpdateMfaPolicy } from './mfa-policy-api';
import { MfaFactor, type MfaPolicyDto, type CreateMfaPolicyRequest } from './mfa-policy-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { appMessage } from '@/core/api/notification-bridge';

// ─── Form shape ──────────────────────────────────────────────────────────────

interface PolicyFormValues {
  role: string;
  isMandatory: boolean;
  graceDays: number;
  totp: boolean;     // AllowedFactors bit 1
  emailOtp: boolean; // AllowedFactors bit 2
}

function factorsFromForm(values: PolicyFormValues): MfaFactor {
  let f: MfaFactor = MfaFactor.None;
  if (values.totp) f = (f | MfaFactor.Totp) as MfaFactor;
  if (values.emailOtp) f = (f | MfaFactor.EmailOtp) as MfaFactor;
  return f;
}

function formFromPolicy(policy: MfaPolicyDto): Partial<PolicyFormValues> {
  return {
    role: policy.role,
    isMandatory: policy.isMandatory,
    graceDays: policy.graceDays,
    totp: (policy.allowedFactors & MfaFactor.Totp) !== 0,
    emailOtp: (policy.allowedFactors & MfaFactor.EmailOtp) !== 0,
  };
}

// ─── Component ───────────────────────────────────────────────────────────────

/**
 * MfaPolicyPage — admin UI for per-role MFA policy management (QĐ742 1.6.a).
 * Route: /admin/mfa-policies (requires SECURITY.MFA_POLICY.VIEW permission).
 * Edit requires SECURITY.MFA_POLICY.MANAGE (enforced by [RequiresMfa] + authorize policy on BE).
 */
export function MfaPolicyPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [editingPolicy, setEditingPolicy] = useState<MfaPolicyDto | null>(null);
  const [form] = Form.useForm<PolicyFormValues>();

  const { data: policies, isLoading } = useMfaPolicies();
  const createPolicy = useCreateMfaPolicy();
  const updatePolicy = useUpdateMfaPolicy();

  const total = policies?.length ?? 0;

  // ── Table columns ─────────────────────────────────────────────────────────

  const columns: ColumnsType<MfaPolicyDto> = [
    {
      title: t('mfaPolicy.role', 'Vai trò'),
      dataIndex: 'role',
      key: 'role',
      render: (role: string) => <Tag color="blue">{role}</Tag>,
    },
    {
      title: (
        <Space>
          {t('mfaPolicy.mandatory', 'Bắt buộc')}
          <Tooltip title={t('mfaPolicy.mandatoryHelp', 'Người dùng với vai trò này phải kích hoạt MFA trong thời gian ân hạn hoặc bị từ chối truy cập.')}>
            <InfoCircleOutlined style={{ color: '#8c8c8c' }} />
          </Tooltip>
        </Space>
      ),
      dataIndex: 'isMandatory',
      key: 'isMandatory',
      width: 130,
      render: (v: boolean) =>
        v ? <Tag color="red">{t('mfaPolicy.required', 'Bắt buộc')}</Tag>
          : <Tag color="default">{t('mfaPolicy.optional', 'Tùy chọn')}</Tag>,
    },
    {
      title: (
        <Space>
          {t('mfaPolicy.graceDays', 'Ân hạn (ngày)')}
          <Tooltip title={t('mfaPolicy.graceDaysHelp', 'Số ngày kể từ khi tạo tài khoản cho phép truy cập trước khi bắt buộc kích hoạt MFA. 0 = phải kích hoạt ngay.')}>
            <InfoCircleOutlined style={{ color: '#8c8c8c' }} />
          </Tooltip>
        </Space>
      ),
      dataIndex: 'graceDays',
      key: 'graceDays',
      width: 150,
      render: (days: number) => days === 0
        ? <Tag color="orange">{t('mfaPolicy.immediate', 'Ngay lập tức')}</Tag>
        : `${days} ${t('mfaPolicy.days', 'ngày')}`,
    },
    {
      title: t('mfaPolicy.allowedFactors', 'Phương thức cho phép'),
      dataIndex: 'allowedFactors',
      key: 'allowedFactors',
      render: (factors: MfaFactor) => (
        <Space wrap>
          {(factors & MfaFactor.Totp) !== 0 && <Tag color="green">TOTP (Authenticator)</Tag>}
          {(factors & MfaFactor.EmailOtp) !== 0 && <Tag color="cyan">Email OTP</Tag>}
          {factors === MfaFactor.None && <Tag color="default">{t('mfaPolicy.noFactors', 'Không cấu hình')}</Tag>}
        </Space>
      ),
    },
    {
      title: t('mfaPolicy.updatedAt', 'Cập nhật lần cuối'),
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 160,
      render: (v: string | null) =>
        v ? new Date(v).toLocaleDateString('vi-VN') : '-',
    },
    {
      title: '',
      key: 'actions',
      width: 60,
      render: (_: unknown, record: MfaPolicyDto) => (
        <Button
          size="small"
          icon={<EditOutlined />}
          onClick={() => openEdit(record)}
          title={t('common.edit', 'Sửa')}
        />
      ),
    },
  ];

  // ── Handlers ──────────────────────────────────────────────────────────────

  function openCreate() {
    setEditingPolicy(null);
    form.resetFields();
    form.setFieldsValue({ isMandatory: true, graceDays: 7, totp: true, emailOtp: false });
    setModalOpen(true);
  }

  function openEdit(policy: MfaPolicyDto) {
    setEditingPolicy(policy);
    form.setFieldsValue(formFromPolicy(policy));
    setModalOpen(true);
  }

  async function handleSave() {
    let values: PolicyFormValues;
    try {
      values = await form.validateFields();
    } catch {
      return; // antd shows inline errors
    }

    const allowedFactors = factorsFromForm(values);
    try {
      if (editingPolicy) {
        await updatePolicy.mutateAsync({
          id: editingPolicy.id,
          isMandatory: values.isMandatory,
          graceDays: values.graceDays,
          allowedFactors,
        });
      } else {
        const body: CreateMfaPolicyRequest = {
          role: values.role,
          isMandatory: values.isMandatory,
          graceDays: values.graceDays,
          allowedFactors,
        };
        await createPolicy.mutateAsync(body);
      }
      appMessage.success(t('common.operationSuccess', 'Thao tác thành công'));
      setModalOpen(false);
      form.resetFields();
      setEditingPolicy(null);
    } catch {
      appMessage.error(t('common.operationFailed', 'Thao tác thất bại'));
    }
  }

  function handleCancel() {
    setModalOpen(false);
    form.resetFields();
    setEditingPolicy(null);
  }

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div>
      <AdminPageHeader
        title={t('mfaPolicy.pageTitle', 'Chính sách MFA')}
        stats={{ total, label: t('mfaPolicy.policies', 'chính sách') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('common.add', 'Thêm')}
          </Button>
        }
      />

      <AdminContentCard noPadding>
        <Table<MfaPolicyDto>
          rowKey="id"
          columns={columns}
          dataSource={policies ?? []}
          loading={isLoading}
          size="small"
          pagination={false}
          style={{ padding: '0 0 8px' }}
        />
      </AdminContentCard>

      {/* Create / Edit Modal */}
      <Modal
        title={
          editingPolicy
            ? t('mfaPolicy.editTitle', 'Sửa chính sách MFA — {{role}}', { role: editingPolicy.role })
            : t('mfaPolicy.createTitle', 'Tạo chính sách MFA')
        }
        open={modalOpen}
        onOk={handleSave}
        onCancel={handleCancel}
        confirmLoading={createPolicy.isPending || updatePolicy.isPending}
        okText={t('common.save', 'Lưu')}
        cancelText={t('common.cancel', 'Hủy')}
        destroyOnHidden
        width={480}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          {/* Role — only editable on create; locked on edit to prevent orphaned rows */}
          <Form.Item
            name="role"
            label={t('mfaPolicy.role', 'Vai trò')}
            rules={[
              { required: true, message: t('common.required', 'Bắt buộc') },
              { max: 100, message: t('common.validation.maxLength', 'Không được vượt quá {{max}} ký tự.', { max: 100 }) },
            ]}
          >
            <input
              disabled={!!editingPolicy}
              className="ant-input"
              style={{ width: '100%' }}
              placeholder="SystemAdmin"
            />
          </Form.Item>

          <Divider orientation="left" orientationMargin={0} style={{ marginTop: 0 }}>
            {t('mfaPolicy.enforcement', 'Thực thi')}
          </Divider>

          <Form.Item
            name="isMandatory"
            label={t('mfaPolicy.mandatory', 'Bắt buộc MFA')}
            valuePropName="checked"
            tooltip={t('mfaPolicy.mandatoryHelp', 'Bật = người dùng phải kích hoạt MFA trong ân hạn; Tắt = tùy chọn.')}
          >
            <Switch
              checkedChildren={t('mfaPolicy.required', 'Bắt buộc')}
              unCheckedChildren={t('mfaPolicy.optional', 'Tùy chọn')}
            />
          </Form.Item>

          <Form.Item
            name="graceDays"
            label={t('mfaPolicy.graceDays', 'Ân hạn (ngày)')}
            tooltip={t('mfaPolicy.graceDaysHelp', '0 = phải kích hoạt ngay. SystemAdmin nên đặt 0 để tránh self-lockout.')}
            rules={[
              { required: true, message: t('common.required', 'Bắt buộc') },
              { type: 'number', min: 0, max: 365, message: t('mfaPolicy.graceDaysRange', '0–365 ngày') },
            ]}
          >
            <InputNumber min={0} max={365} style={{ width: '100%' }} />
          </Form.Item>

          <Divider orientation="left" orientationMargin={0}>
            {t('mfaPolicy.allowedFactors', 'Phương thức cho phép')}
          </Divider>

          <Form.Item
            name="totp"
            valuePropName="checked"
          >
            <Checkbox>
              TOTP (Google Authenticator, Microsoft Authenticator, v.v.)
            </Checkbox>
          </Form.Item>

          <Form.Item
            name="emailOtp"
            valuePropName="checked"
          >
            <Checkbox>
              Email OTP
            </Checkbox>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
