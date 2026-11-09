// form-preview-modal.tsx — interactive form preview with multi-step + conditional visibility
// Section fields act as step dividers; visibility rules evaluated from visibilityRulesJson

import { useState, useMemo } from 'react';
import { Modal, Form, Steps, Button, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FormFieldDto } from '../form-types';
import { isFieldVisible } from '../form-visibility-helpers';
import { renderInteractiveField } from './form-field-renderer';
import { splitIntoSteps } from '../form-step-utils';
import { appMessage } from '@/core/api/notification-bridge';

const { Text } = Typography;

// ---------------------------------------------------------------------------
// Modal component
// ---------------------------------------------------------------------------

interface FormPreviewModalProps {
  open: boolean;
  onClose: () => void;
  fields: FormFieldDto[];
  templateName: string;
}

export function FormPreviewModal({
  open,
  onClose,
  fields,
  templateName,
}: FormPreviewModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<Record<string, unknown>>();
  const [currentStep, setCurrentStep] = useState(0);
  // Extra values not tracked by antd form (e.g. uploaded file IDs)
  const [extraValues, setExtraValues] = useState<Record<string, unknown>>({});

  // Top-level fields only: skip TableField child columns (parentFieldId != null) - they render
  // inside TableFieldWidget's per-row Table, never as standalone Form.Items in the preview flow.
  // See form-submit-modal.tsx for the same guard rationale.
  const sorted = useMemo(
    () => [...fields]
      .filter((f) => f.parentFieldId == null)
      .sort((a, b) => a.displayOrder - b.displayOrder),
    [fields]
  );

  const steps = useMemo(() => splitIntoSteps(sorted), [sorted]);
  const isMultiStep = steps.length > 1;
  const isLastStep = currentStep === steps.length - 1;

  // Merge antd form values + extra values for visibility evaluation
  function getMergedValues(): Record<string, unknown> {
    return { ...form.getFieldsValue(), ...extraValues };
  }

  function handleFileUploaded(fieldKey: string, fileId: string) {
    setExtraValues((prev) => ({ ...prev, [fieldKey]: fileId }));
  }

  function handleNext() {
    const fieldNames = currentFields
      .filter((f) => isFieldVisible(f.visibilityRulesJson, mergedValues))
      .map((f) => f.fieldKey);
    form.validateFields(fieldNames).then(() => {
      setCurrentStep((s) => Math.min(s + 1, steps.length - 1));
    }).catch(() => {
      // validation errors shown inline
    });
  }

  function handlePrev() {
    setCurrentStep((s) => Math.max(s - 1, 0));
  }

  function handleSubmit() {
    form.validateFields().then(() => {
      getMergedValues();
      // In preview mode we just show success; real submission would call an API
      appMessage.success(t('forms.preview.submitSuccess', { defaultValue: 'Form submitted (preview)' }));
      onClose();
    }).catch(() => {});
  }

  function handleClose() {
    form.resetFields();
    setCurrentStep(0);
    setExtraValues({});
    onClose();
  }

  const currentFields = steps[currentStep]?.fields ?? [];
  const mergedValues = getMergedValues();

  const footer = (
    <Space style={{ justifyContent: 'flex-end', width: '100%' }}>
      {isMultiStep && currentStep > 0 && (
        <Button onClick={handlePrev}>
          {t('forms.preview.prev', { defaultValue: 'Previous' })}
        </Button>
      )}
      {isMultiStep && !isLastStep && (
        <Button type="primary" onClick={handleNext}>
          {t('forms.preview.next', { defaultValue: 'Next' })}
        </Button>
      )}
      {(!isMultiStep || isLastStep) && (
        <Button type="primary" onClick={handleSubmit}>
          {t('forms.preview.submit', { defaultValue: 'Submit' })}
        </Button>
      )}
    </Space>
  );

  return (
    <Modal
      open={open}
      onCancel={handleClose}
      footer={footer}
      width="70%"
      centered
      title={`${t('forms.builder.toolbar.preview')}: ${templateName}`}
      styles={{ body: { maxHeight: '75vh', overflowY: 'auto', padding: '16px 24px' } }}
      destroyOnHidden
    >
      {/* Step indicator — only shown when multiple steps */}
      {isMultiStep && (
        <Steps
          current={currentStep}
          size="small"
          style={{ marginBottom: 24 }}
          items={steps.map((s) => ({ title: s.title }))}
        />
      )}

      <Form form={form} layout="vertical">
        {currentFields.map((field) => {
          const visible = isFieldVisible(field.visibilityRulesJson, mergedValues);
          if (!visible) return null;
          return renderInteractiveField({
            field,
            onFileUploaded: handleFileUploaded,
            t,
          });
        })}
        {currentFields.length === 0 && (
          <Text type="secondary">{t('forms.preview.emptyStep', { defaultValue: 'No fields in this step.' })}</Text>
        )}
      </Form>
    </Modal>
  );
}
