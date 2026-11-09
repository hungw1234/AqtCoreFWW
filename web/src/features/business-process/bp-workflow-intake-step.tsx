// bp-workflow-intake-step.tsx — Wizard step 2: workflow picker + intake form picker + version
import { Form, Select, InputNumber } from 'antd';
import { useTranslation } from 'react-i18next';
import { useWorkflowDefinitionPicker, useFormTemplatePicker } from './business-process-admin-api';

export interface BpWorkflowIntakeValues {
  workflowDefinitionPublicId: string;
  intakeFormTemplatePublicId?: string | null;
  intakeFormTemplateVersion?: number | null;
}

interface BpWorkflowIntakeStepProps {
  form: ReturnType<typeof Form.useForm<BpWorkflowIntakeValues>>[0];
  intakeSelected: boolean;
  onIntakeChange: (selected: boolean) => void;
}

export function BpWorkflowIntakeStep({ form, intakeSelected, onIntakeChange }: BpWorkflowIntakeStepProps) {
  const { t } = useTranslation();

  const { data: wfData, isLoading: wfLoading } = useWorkflowDefinitionPicker();
  const { data: ftData, isLoading: ftLoading } = useFormTemplatePicker();

  const wfOptions = (wfData?.items ?? []).map((w) => ({
    label: `${w.name} (${w.definitionKey})`,
    value: w.id,
  }));

  const ftOptions = (ftData?.items ?? []).map((f) => ({
    label: `${f.name} (${f.code})`,
    value: f.id,
  }));

  function handleIntakeChange(val: string | null | undefined) {
    if (val) {
      const selected = (ftData?.items ?? []).find((f) => f.id === val);
      if (selected?.version != null) {
        form.setFieldValue('intakeFormTemplateVersion', selected.version);
      }
      onIntakeChange(true);
    } else {
      form.setFieldValue('intakeFormTemplateVersion', null);
      onIntakeChange(false);
    }
  }

  return (
    <Form form={form} layout="vertical" style={{ maxWidth: 560 }}>
      <Form.Item
        name="workflowDefinitionPublicId"
        label={t('page.businessProcessAdmin.fieldWorkflow', { defaultValue: 'Luồng xử lý (Workflow)' })}
        rules={[
          { required: true, message: t('page.businessProcessAdmin.workflowRequired', { defaultValue: 'Vui lòng chọn luồng xử lý' }) },
        ]}
      >
        <Select
          placeholder={t('page.businessProcessAdmin.workflowPlaceholder', { defaultValue: 'Chọn workflow...' })}
          options={wfOptions}
          loading={wfLoading}
          showSearch
          optionFilterProp="label"
          style={{ width: '100%' }}
        />
      </Form.Item>

      <Form.Item
        name="intakeFormTemplatePublicId"
        label={t('page.businessProcessAdmin.fieldIntakeForm', { defaultValue: 'Biểu mẫu tiếp nhận (tùy chọn)' })}
      >
        <Select
          placeholder={t('page.businessProcessAdmin.intakeFormPlaceholder', { defaultValue: 'Chọn biểu mẫu tiếp nhận (không bắt buộc)...' })}
          options={ftOptions}
          loading={ftLoading}
          showSearch
          optionFilterProp="label"
          allowClear
          style={{ width: '100%' }}
          onChange={handleIntakeChange}
        />
      </Form.Item>

      {intakeSelected && (
        <Form.Item
          name="intakeFormTemplateVersion"
          label={t('page.businessProcessAdmin.fieldIntakeVersion', { defaultValue: 'Phiên bản biểu mẫu' })}
          rules={[
            { required: true, message: t('page.businessProcessAdmin.versionRequired', { defaultValue: 'Vui lòng nhập phiên bản' }) },
            { type: 'number', min: 1, message: t('page.businessProcessAdmin.versionMin', { defaultValue: 'Phiên bản phải >= 1' }) },
          ]}
        >
          <InputNumber min={1} style={{ width: 160 }} />
        </Form.Item>
      )}
    </Form>
  );
}
