// bp-basics-step.tsx — Wizard step 1: code, name, description
import { Form, Input } from 'antd';
import { useTranslation } from 'react-i18next';

export interface BpBasicsValues {
  code: string;
  name: string;
  description?: string;
}

interface BpBasicsStepProps {
  form: ReturnType<typeof Form.useForm<BpBasicsValues>>[0];
}

export function BpBasicsStep({ form }: BpBasicsStepProps) {
  const { t } = useTranslation();

  return (
    <Form form={form} layout="vertical" style={{ maxWidth: 560 }}>
      <Form.Item
        name="code"
        label={t('page.businessProcessAdmin.fieldCode', { defaultValue: 'Mã quy trình' })}
        rules={[
          { required: true, message: t('page.businessProcessAdmin.codeRequired', { defaultValue: 'Vui lòng nhập mã quy trình' }) },
          { max: 100, message: t('common.validation.maxLength', { max: 100, defaultValue: 'Không được vượt quá 100 ký tự.' }) },
          { pattern: /^[A-Z0-9_]+$/, message: t('common.validation.pattern.upperAlphaNum', { defaultValue: 'Chỉ được chứa chữ hoa, số và dấu gạch dưới.' }) },
        ]}
      >
        <Input
          placeholder="VD: ONBOARDING_NV"
          maxLength={100}
          style={{ textTransform: 'uppercase' }}
          onChange={(e) => form.setFieldValue('code', e.target.value.toUpperCase())}
        />
      </Form.Item>

      <Form.Item
        name="name"
        label={t('page.businessProcessAdmin.fieldName', { defaultValue: 'Tên quy trình' })}
        rules={[
          { required: true, message: t('page.businessProcessAdmin.nameRequired', { defaultValue: 'Vui lòng nhập tên quy trình' }) },
          { max: 200, message: t('common.validation.maxLength', { max: 200, defaultValue: 'Không được vượt quá 200 ký tự.' }) },
        ]}
      >
        <Input
          placeholder={t('page.businessProcessAdmin.namePlaceholder', { defaultValue: 'VD: Onboarding nhân viên mới' })}
          maxLength={200}
        />
      </Form.Item>

      <Form.Item
        name="description"
        label={t('page.businessProcessAdmin.fieldDescription', { defaultValue: 'Mô tả' })}
        rules={[
          { max: 1000, message: t('common.validation.maxLength', { max: 1000, defaultValue: 'Không được vượt quá 1000 ký tự.' }) },
        ]}
      >
        <Input.TextArea
          placeholder={t('page.businessProcessAdmin.descriptionPlaceholder', { defaultValue: 'Mô tả ngắn về quy trình (tùy chọn)' })}
          rows={3}
          maxLength={1000}
          showCount
        />
      </Form.Item>
    </Form>
  );
}
