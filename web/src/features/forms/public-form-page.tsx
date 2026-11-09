// public-form-page.tsx — renders an active form for anonymous users (no auth required)
// Multi-step wizard when Section fields present; visibility rules + client-side validation applied

import { useState } from 'react';
import { Form, Button, Alert, Typography, Checkbox, Steps, Space } from 'antd';
import { CenteredSpin } from '@/shared/components/centered-spin';
import { useParams } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { usePublicForm, useSubmitPublicForm, useEvaluatePublicFormulas } from './form-api';
import { renderInteractiveField } from './builder/form-field-renderer';
import { useFormulaValues } from './use-formula-values';
import { splitIntoSteps, buildValidationRules, buildRequiredIfRules } from './form-step-utils';
import { isFieldVisible } from './form-visibility-helpers';
import { cleanFormData } from './form-submission-helpers';
import { appMessage } from '@/core/api/notification-bridge';

const { Title } = Typography;

// PublicFormPage — renders at /public/forms/:code (outside authenticated layout)
export function PublicFormPage() {
  const { t } = useTranslation();
  const { code } = useParams({ strict: false }) as { code: string };
  const [submitted, setSubmitted] = useState(false);
  const [form] = Form.useForm<Record<string, unknown>>();
  const [currentStep, setCurrentStep] = useState(0);
  const [isValidating, setIsValidating] = useState(false);

  // Must be called unconditionally (hooks rule) — returns undefined until form mounts
  const formValues = (Form.useWatch([], form) ?? {}) as Record<string, unknown>;

  const { data: template, isLoading, isError } = usePublicForm(code);
  const { mutate: submit, isPending } = useSubmitPublicForm(code);

  // Reactive Formula compute: watch-all values → debounced public BE evaluate → display-only map.
  // Called unconditionally (hooks rule); no-ops until the template loads a Formula field.
  const hasFormula = (template?.fields ?? []).some((f) => f.isActive && f.type === 'Formula');
  const evaluate = useEvaluatePublicFormulas(code);
  const computedValues = useFormulaValues({
    formValues,
    hasFormula,
    evaluate: (v) => evaluate.mutateAsync(v),
  });

  if (isLoading) {
    return (
      <CenteredSpin
        tip={t('common.loading', { defaultValue: 'Loading...' })}
        style={{ display: 'block', marginTop: 48 }}
      />
    );
  }

  if (isError || !template) {
    return (
      <Alert
        type="error"
        message={t('page.forms.public.notFound', { defaultValue: 'Form not found or unavailable.' })}
      />
    );
  }

  // Top-level fields only: skip TableField child columns (parentFieldId != null) - they are
  // rendered inside TableFieldWidget's per-row Table, never as standalone top-level Form.Items.
  // See form-submit-modal.tsx for the same guard rationale.
  const activeFields = [...(template.fields ?? [])]
    .filter((f) => f.isActive && f.parentFieldId == null)
    .sort((a, b) => a.displayOrder - b.displayOrder);

  const steps = splitIntoSteps(activeFields);
  const isMultiStep = steps.length > 1;
  const isLastStep = currentStep === steps.length - 1;

  // Filter visible fields for current step using current form values
  const currentFields = steps[currentStep].fields.filter((f) =>
    isFieldVisible(f.visibilityRulesJson, formValues)
  );

  async function handleNext() {
    if (isValidating) return;
    setIsValidating(true);
    try {
      const fieldNames = currentFields.map((f) => f.fieldKey);
      await form.validateFields(fieldNames);
      setCurrentStep((s) => s + 1);
    } catch {
      // Validation errors shown inline — do not advance
    } finally {
      setIsValidating(false);
    }
  }

  function handleFinish(values: Record<string, unknown>) {
    const { __consentGiven, ...rawData } = values as Record<string, unknown> & {
      __consentGiven?: boolean;
    };
    // Clean: strip hidden fields, UI-only types, __rowKey from TableField
    const data = cleanFormData(rawData, template?.fields ?? [], formValues);
    submit(
      {
        data,
        ...(template?.requiresConsent ? { consentGiven: Boolean(__consentGiven) } : {}),
      },
      {
        onSuccess: () => setSubmitted(true),
        onError: () =>
          appMessage.error(
            t('page.forms.public.submitFailed', {
              defaultValue: 'Submission failed. Please try again.',
            })
          ),
      }
    );
  }

  if (submitted) {
    return (
      <div style={{ maxWidth: 600, margin: '48px auto', padding: 24 }}>
        <Alert
          type="success"
          message={t('page.forms.public.successTitle', { defaultValue: 'Submitted successfully' })}
          description={t('page.forms.public.successDesc', {
            defaultValue: 'Your response has been recorded.',
          })}
          showIcon
        />
      </div>
    );
  }

  return (
    <div style={{ maxWidth: 600, margin: '48px auto', padding: 24 }}>
      <Title level={3}>{template.nameVi || template.name}</Title>

      {/* Step indicator — only shown when multiple steps exist */}
      {isMultiStep && (
        <Steps
          current={currentStep}
          size="small"
          style={{ marginBottom: 24 }}
          items={steps.map((s) => ({ title: s.title }))}
        />
      )}

      <Form form={form} layout="vertical" onFinish={handleFinish}>
        {currentFields.map((field) =>
          renderInteractiveField({
            field,
            templateId: template.id,
            allFields: template.fields,
            computedValues,
            // Anonymous form: reference-field options resolve through the public {code}/{fieldKey} path
            // (no Bearer). Without this, RefFieldSelect would hit the authed template endpoint → 401.
            publicCode: code,
            extraRules: [
              ...buildValidationRules(field, t),
              ...buildRequiredIfRules(field, formValues, t),
            ],
            t,
          })
        )}

        {/* Consent checkbox — shown only on final step when template requiresConsent */}
        {isLastStep && template.requiresConsent && (
          <Form.Item
            name="__consentGiven"
            valuePropName="checked"
            rules={[
              {
                validator: (_, value) =>
                  value
                    ? Promise.resolve()
                    : Promise.reject(
                        t('page.forms.public.consentRequired', {
                          defaultValue: 'You must agree to the consent terms to submit.',
                        })
                      ),
              },
            ]}
            style={{ marginTop: 16 }}
          >
            <Checkbox>
              {template.consentText ||
                t('page.forms.public.defaultConsent', {
                  defaultValue: 'I agree to the terms and consent to data processing.',
                })}
            </Checkbox>
          </Form.Item>
        )}

        <Form.Item style={{ marginTop: 24 }}>
          <Space>
            {/* Back button — shown on steps 2+ */}
            {currentStep > 0 && (
              <Button onClick={() => setCurrentStep((s) => s - 1)}>
                {t('common.previous', { defaultValue: 'Previous' })}
              </Button>
            )}
            {/* Next button — shown on non-final steps in multi-step mode */}
            {isMultiStep && !isLastStep && (
              <Button type="primary" onClick={handleNext}>
                {t('common.next', { defaultValue: 'Next' })}
              </Button>
            )}
            {/* Submit button — shown on final (or only) step */}
            {isLastStep && (
              <Button
                type="primary"
                htmlType="submit"
                loading={isPending}
                block={!isMultiStep}
              >
                {t('common.submit', { defaultValue: 'Submit' })}
              </Button>
            )}
          </Space>
        </Form.Item>
      </Form>
    </div>
  );
}
