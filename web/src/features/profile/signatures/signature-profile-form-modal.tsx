import { useEffect } from 'react';
import { Modal, Form, Input, Switch } from 'antd';
import { useTranslation } from 'react-i18next';

export type ProfileFormMode = 'create' | 'edit';

/** The values the profile form collects — display name + the two per-profile stamp visibility toggles. */
export interface ProfileFormValues {
  displayName: string;
  showSerial: boolean;
  showIssuer: boolean;
}

interface SignatureProfileFormModalProps {
  open: boolean;
  mode: ProfileFormMode;
  /** Pre-fills the form in edit mode (display name + stamp toggles). Undefined → create defaults. */
  initialValues?: ProfileFormValues;
  confirmLoading?: boolean;
  onSubmit: (values: ProfileFormValues) => void;
  onCancel: () => void;
}

// New profiles default both stamp lines ON (matches the BE entity/column default).
const CREATE_DEFAULTS: ProfileFormValues = { displayName: '', showSerial: true, showIssuer: true };

/**
 * Create/edit modal for a signature profile: display name + whether the visible PAdES stamp shows the
 * Serial / Nhà CC lines. Form state resets on each open so a prior create's input never bleeds into an edit.
 */
export function SignatureProfileFormModal({
  open,
  mode,
  initialValues,
  confirmLoading,
  onSubmit,
  onCancel,
}: SignatureProfileFormModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<ProfileFormValues>();

  useEffect(() => {
    if (open) form.setFieldsValue(initialValues ?? CREATE_DEFAULTS);
  }, [open, initialValues, form]);

  async function handleOk() {
    try {
      const values = await form.validateFields();
      onSubmit({
        displayName: values.displayName.trim(),
        showSerial: values.showSerial,
        showIssuer: values.showIssuer,
      });
    } catch {
      // validateFields rejects on invalid input — antd renders the field errors, nothing else to do.
    }
  }

  return (
    <Modal
      open={open}
      title={t(mode === 'create' ? 'profile.signatures.createTitle' : 'profile.signatures.editTitle')}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={confirmLoading}
      onOk={handleOk}
      onCancel={onCancel}
      destroyOnClose
    >
      <Form form={form} layout="vertical" initialValues={CREATE_DEFAULTS}>
        <Form.Item
          name="displayName"
          label={t('profile.signatures.col.name')}
          rules={[
            { required: true, message: t('profile.signatures.nameRequired') },
            { max: 100, message: t('profile.signatures.nameMax') },
          ]}
        >
          <Input maxLength={100} placeholder={t('profile.signatures.namePlaceholder')} />
        </Form.Item>

        <Form.Item
          name="showSerial"
          label={t('profile.signatures.showSerial')}
          tooltip={t('profile.signatures.showSerialHint')}
          valuePropName="checked"
        >
          <Switch />
        </Form.Item>

        <Form.Item
          name="showIssuer"
          label={t('profile.signatures.showIssuer')}
          tooltip={t('profile.signatures.showIssuerHint')}
          valuePropName="checked"
        >
          <Switch />
        </Form.Item>
      </Form>
    </Modal>
  );
}
