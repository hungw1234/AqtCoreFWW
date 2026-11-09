// form-submit-modal.tsx — authenticated submit modal with multi-step + validation + visibility
import { useState } from 'react';
import { Modal, Form, Steps, Button, Space, Checkbox } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FormTemplateDto } from './form-types';
import { useSubmitForm, useEvaluateFormulas } from './form-api';
import { renderInteractiveField } from './builder/form-field-renderer';
import { useFormulaValues } from './use-formula-values';
import { splitIntoSteps, buildValidationRules, buildRequiredIfRules } from './form-step-utils';
import { isFieldVisible } from './form-visibility-helpers';
import { cleanFormData, extractSignaturePayload } from './form-submission-helpers';
import { useSigningProviderInfo } from '@/features/digital-signature/signature-api';
import { appMessage } from '@/core/api/notification-bridge';

interface FormSubmitModalProps {
  template: FormTemplateDto;
  open: boolean;
  onClose: () => void;
}

export function FormSubmitModal({ template, open, onClose }: FormSubmitModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm();
  const submit = useSubmitForm();
  const [currentStep, setCurrentStep] = useState(0);
  const formValues = Form.useWatch([], form) ?? {};

  // Resolve the tenant's signing provider only when the template actually needs it.
  const hasDigitalSignature = template.fields.some(
    (f) => f.isActive && f.type === 'DigitalSignature',
  );
  const { data: signingProvider } = useSigningProviderInfo(hasDigitalSignature);

  // Reactive Formula compute: watch-all values → debounced BE evaluate → display-only map.
  const hasFormula = template.fields.some((f) => f.isActive && f.type === 'Formula');
  const evaluate = useEvaluateFormulas();
  const computedValues = useFormulaValues({
    formValues: formValues as Record<string, unknown>,
    hasFormula,
    evaluate: (v) => evaluate.mutateAsync({ templatePublicId: template.id, values: v }),
  });

  // Top-level fields only: skip TableField child columns (parentFieldId != null). Child columns
  // are rendered INSIDE TableFieldWidget's per-row Table; if included here they would double-render
  // AND collide on antd Form.Item name (same fieldKey used twice). Backward-safe: legacy fixtures
  // without parentFieldId keep all fields top-level.
  const activeFields = [...template.fields]
    .filter((f) => f.isActive && f.parentFieldId == null)
    .sort((a, b) => a.displayOrder - b.displayOrder);

  const steps = splitIntoSteps(activeFields);
  const isMultiStep = steps.length > 1;

  const currentFields = steps[currentStep]?.fields.filter(
    (f) => isFieldVisible(f.visibilityRulesJson, formValues as Record<string, unknown>)
  ) ?? [];

  const [isValidating, setIsValidating] = useState(false);

  async function handleNext() {
    if (isValidating) return;
    setIsValidating(true);
    try {
      const fieldNames = currentFields.map((f) => f.fieldKey);
      await form.validateFields(fieldNames);
      setCurrentStep((s) => s + 1);
    } catch {
      // validation errors shown inline
    } finally {
      setIsValidating(false);
    }
  }

  function handleSubmit(values: Record<string, unknown>) {
    if (template.status !== 'Active') {
      appMessage.error(t('page.forms.templateInactive', { defaultValue: 'Form is no longer active' }));
      return;
    }
    const { __consentGiven, ...rawData } = values as Record<string, unknown> & { __consentGiven?: boolean };
    const data = cleanFormData(rawData, template.fields, formValues as Record<string, unknown>);
    const signature = extractSignaturePayload(values, template.fields);
    submit.mutate(
      {
        formTemplatePublicId: template.id,
        data,
        ...(template.requiresConsent ? { consentGiven: Boolean(__consentGiven) } : {}),
        ...(signature ? { signature } : {}),
      },
      {
        onSuccess: () => {
          appMessage.success(t('page.forms.submitSuccess', { defaultValue: 'Form submitted successfully' }));
          onClose();
          form.resetFields();
          setCurrentStep(0);
        },
        onError: () => appMessage.error(t('page.forms.submitFailed', { defaultValue: 'Submit failed' })),
      }
    );
  }

  function handleClose() {
    form.resetFields();
    setCurrentStep(0);
    onClose();
  }

  return (
    <Modal
      title={template.nameVi || template.name}
      open={open}
      onCancel={handleClose}
      width={720}
      footer={null}
      destroyOnHidden
    >
      {isMultiStep && (
        <Steps
          current={currentStep}
          size="small"
          style={{ marginBottom: 16 }}
          items={steps.map((s) => ({ title: s.title }))}
        />
      )}

      <Form form={form} layout="vertical" onFinish={handleSubmit}>
        {currentFields.map((field) =>
          renderInteractiveField({
            field,
            templateId: template.id,
            allFields: template.fields,
            signingProvider,
            computedValues,
            extraRules: [
              ...buildValidationRules(field, t),
              ...buildRequiredIfRules(field, formValues as Record<string, unknown>, t),
            ],
            t,
          })
        )}

        {/* Consent — last step only */}
        {currentStep === steps.length - 1 && template.requiresConsent && (
          <Form.Item
            name="__consentGiven"
            valuePropName="checked"
            rules={[{
              validator: (_, v) =>
                v ? Promise.resolve() : Promise.reject(t('forms.public.consentRequired', { defaultValue: 'You must agree to consent terms' })),
            }]}
          >
            <Checkbox>
              {template.consentText || t('forms.public.defaultConsent', { defaultValue: 'I agree to the terms and consent to data processing.' })}
            </Checkbox>
          </Form.Item>
        )}

        <Space style={{ marginTop: 16 }}>
          {currentStep > 0 && (
            <Button onClick={() => setCurrentStep((s) => s - 1)}>
              {t('common.previous', { defaultValue: 'Previous' })}
            </Button>
          )}
          {isMultiStep && currentStep < steps.length - 1 && (
            <Button type="primary" onClick={handleNext}>
              {t('common.next', { defaultValue: 'Next' })}
            </Button>
          )}
          {currentStep === steps.length - 1 && (
            <Button type="primary" htmlType="submit" loading={submit.isPending}>
              {t('common.submit', { defaultValue: 'Submit' })}
            </Button>
          )}
        </Space>
      </Form>
    </Modal>
  );
}
