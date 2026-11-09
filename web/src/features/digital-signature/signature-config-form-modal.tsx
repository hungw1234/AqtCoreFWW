/**
 * signature-config-form-modal.tsx — Edit modal for tenant signature config.
 * Allows admin to set active provider, Vault credentials ref, CA thumbprints,
 * OCSP mode, and enable/disable signing.
 *
 * Field mapping verified against BE UpsertSignatureConfigRequest 2026-05-11:
 *   providerKey, apiCredentialsRef, allowedCaRootThumbprints, ocspMode, isEnabled
 * Removed: allowUserOverride (no BE counterpart on UpsertSignatureConfigRequest).
 */

import { useEffect } from 'react';
import { Form, Modal, Select, Switch, Input, Divider } from 'antd';
import { useTranslation } from 'react-i18next';
import type {
  TenantSignatureConfigDto,
  UpdateSignatureConfigRequest,
  AvailableProviderDto,
  OcspMode,
  SignatureFlowMode,
} from './signature-types';

// ─── Form shape ───────────────────────────────────────────────────────────────

interface ConfigFormValues {
  providerKey: string;
  apiCredentialsRef: string | null;
  allowedCaRootThumbprints: string;
  isEnabled: boolean;
  ocspMode: OcspMode;
  signFlowMode: SignatureFlowMode;
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface SignatureConfigFormModalProps {
  open: boolean;
  config: TenantSignatureConfigDto | null;
  providers: AvailableProviderDto[];
  isSubmitting: boolean;
  onSave: (req: UpdateSignatureConfigRequest) => void;
  onCancel: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * SignatureConfigFormModal — update tenant digital-signature configuration.
 * Required permission for save: SECURITY.SIGNATURE_CONFIG.MANAGE (enforced on BE).
 */
export function SignatureConfigFormModal({
  open,
  config,
  providers,
  isSubmitting,
  onSave,
  onCancel,
}: SignatureConfigFormModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<ConfigFormValues>();

  // Populate form when config or open state changes
  useEffect(() => {
    if (open && config) {
      form.setFieldsValue({
        providerKey: config.providerKey,
        apiCredentialsRef: config.apiCredentialsRef ?? null,
        allowedCaRootThumbprints: config.allowedCaRootThumbprints,
        isEnabled: config.isEnabled,
        ocspMode: config.ocspMode,
        signFlowMode: config.signFlowMode,
      });
    } else if (open && !config) {
      form.resetFields();
      // Defaults: disabled, Soft OCSP, Async flow, empty fields
      form.setFieldsValue({ isEnabled: false, ocspMode: 'Soft', signFlowMode: 'Async' });
    }
  }, [open, config, form]);

  async function handleOk() {
    let values: ConfigFormValues;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }
    onSave({
      providerKey: values.providerKey,
      apiCredentialsRef: values.apiCredentialsRef || null,
      allowedCaRootThumbprints: values.allowedCaRootThumbprints ?? '',
      ocspMode: values.ocspMode,
      isEnabled: values.isEnabled,
      signFlowMode: values.signFlowMode ?? 'Async',
      rowVersion: config?.rowVersion ?? null,  // forward concurrency token from prior GET
    });
  }

  // OcspMode options matching BE enum { Soft, Hard }
  const ocspOptions: { value: OcspMode; label: string }[] = [
    { value: 'Soft', label: t('digitalSignature.ocspSoft', 'Soft — OCSP thất bại không chặn ký') },
    { value: 'Hard', label: t('digitalSignature.ocspHard', 'Hard — OCSP thất bại từ chối ký') },
  ];

  const providerOptions = [
    ...providers.map((p) => ({ value: p.providerKey, label: p.displayName || p.providerKey })),
  ];

  // SignatureFlowMode options matching BE enum { Async, Sync }. Only affects server-side remote providers.
  const flowModeOptions: { value: SignatureFlowMode; label: string }[] = [
    { value: 'Async', label: t('digitalSignature.flowAsync', 'Async — không chặn, hoàn tất nền (khuyến nghị)') },
    { value: 'Sync', label: t('digitalSignature.flowSync', 'Sync — chờ OTP ngay trong yêu cầu (~120s)') },
  ];

  return (
    <Modal
      title={t('digitalSignature.editConfigTitle', 'Cấu hình chữ ký số')}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={isSubmitting}
      okText={t('common.save', 'Lưu')}
      cancelText={t('common.cancel', 'Huỷ')}
      destroyOnHidden
      width={520}
    >
      <Form form={form} layout="vertical" style={{ marginTop: 8 }}>

        <Form.Item
          name="isEnabled"
          label={t('digitalSignature.isEnabled', 'Kích hoạt chữ ký số')}
          valuePropName="checked"
          tooltip={t(
            'digitalSignature.isEnabledHelp',
            'Bật để cho phép người dùng ký số tài liệu trong tenant này.',
          )}
        >
          <Switch
            checkedChildren={t('common.active', 'Bật')}
            unCheckedChildren={t('common.inactive', 'Tắt')}
          />
        </Form.Item>

        <Divider orientation="left" orientationMargin={0} style={{ marginTop: 0 }}>
          {t('digitalSignature.providerSection', 'Nhà cung cấp')}
        </Divider>

        <Form.Item
          name="providerKey"
          label={t('digitalSignature.providerKey', 'Nhà cung cấp chữ ký số')}
          rules={[{ required: true, message: t('common.required', 'Bắt buộc') }]}
        >
          <Select
            options={providerOptions}
            placeholder={t('digitalSignature.selectProvider', 'Chọn nhà cung cấp...')}
          />
        </Form.Item>

        <Form.Item
          name="apiCredentialsRef"
          label={t('digitalSignature.apiCredentialsRef', 'Vault credentials ref')}
          tooltip={t(
            'digitalSignature.apiCredentialsRefHelp',
            'Đường dẫn Vault tới credentials API (vd: aqt/signing/vnpt-smartca/api-credentials). Để trống nếu nhà cung cấp không cần xác thực phía máy chủ.',
          )}
        >
          <Input
            placeholder="aqt/signing/vnpt-smartca/api-credentials"
            maxLength={256}
            allowClear
          />
        </Form.Item>

        <Form.Item
          name="allowedCaRootThumbprints"
          label={t('digitalSignature.allowedCaRootThumbprints', 'Root CA thumbprints (SHA-1, CSV)')}
          tooltip={t(
            'digitalSignature.thumbprintsHelp',
            'Danh sách thumbprint SHA-1 của chứng chỉ gốc VN CA được tin cậy, phân cách bởi dấu phẩy. Tối đa 2048 ký tự (~40 thumbprint).',
          )}
        >
          <Input.TextArea
            rows={3}
            maxLength={2048}
            showCount
            placeholder="abc123..., def456..."
          />
        </Form.Item>

        <Divider orientation="left" orientationMargin={0}>
          {t('digitalSignature.verificationSection', 'Xác minh')}
        </Divider>

        <Form.Item
          name="ocspMode"
          label={t('digitalSignature.ocspMode', 'Chế độ OCSP')}
          rules={[{ required: true, message: t('common.required', 'Bắt buộc') }]}
          tooltip={t(
            'digitalSignature.ocspModeHelp',
            'OCSP xác minh trạng thái thu hồi chứng chỉ. Hard = ký thất bại nếu OCSP không phản hồi.',
          )}
        >
          <Select options={ocspOptions} />
        </Form.Item>

        <Form.Item
          name="signFlowMode"
          label={t('digitalSignature.signFlowMode', 'Luồng ký từ xa')}
          rules={[{ required: true, message: t('common.required', 'Bắt buộc') }]}
          tooltip={t(
            'digitalSignature.signFlowModeHelp',
            'Chỉ áp dụng cho nhà cung cấp ký server-side (Viettel Mysign). Async: trả về ngay, hoàn tất qua job nền ' +
              '(khuyến nghị). Sync: yêu cầu chờ tới ~120s cho người ký xác nhận OTP — chấp nhận rủi ro timeout gateway.',
          )}
        >
          <Select options={flowModeOptions} />
        </Form.Item>

      </Form>
    </Modal>
  );
}
