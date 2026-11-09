// form-settings-panel.tsx — Settings tab for form template admin configuration
// Handles anonymous public-access toggle, PDPL consent toggle + text, and approval workflow assignment.

import { useEffect } from 'react';
import { Form, Switch, Select, Input, Button, Space, Typography, Divider, Alert, Modal } from 'antd';
import { useTranslation } from 'react-i18next';
import { useUpdateTemplate, useWorkflowDefinitions, useSetTemplatePublicAccess } from './form-api';
import type { FormTemplateDto } from './form-types';
import { appMessage } from '@/core/api/notification-bridge';

const { Title, Text } = Typography;
const { TextArea } = Input;

interface FormSettingsPanelProps {
  template: FormTemplateDto;
}

/** Settings panel for the form template — consent configuration and approval workflow */
export function FormSettingsPanel({ template }: FormSettingsPanelProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm();

  // Watch consent toggle to show/hide consent text area
  const requiresConsent = Form.useWatch('requiresConsent', form);

  const { mutate: updateTemplate, isPending } = useUpdateTemplate();
  const { mutate: setPublicAccess, isPending: isTogglingPublic } = useSetTemplatePublicAccess();
  const { data: workflowData, isLoading: isLoadingWorkflows } = useWorkflowDefinitions();

  // PII + consent mismatch detection
  const hasPiiFields = template.fields.some((f) => f.isPii && f.isActive);

  // Public-access toggle saves immediately via its own PATCH endpoint (independent of the
  // Save button, which batches consent + workflow through PUT). Enabling is a security-sensitive
  // action (opens the form to anonymous access) so it is gated behind a confirm dialog.
  function handlePublicToggle(next: boolean) {
    const apply = () =>
      setPublicAccess(
        { id: template.id, isPublic: next },
        {
          onSuccess: () =>
            appMessage.success(t('forms.settings.publicSaveSuccess', { defaultValue: 'Public access updated' })),
          onError: () =>
            appMessage.error(t('forms.settings.publicSaveError', { defaultValue: 'Failed to update public access' })),
        }
      );

    if (next) {
      Modal.confirm({
        title: t('forms.settings.publicConfirmTitle', { defaultValue: 'Make this form public?' }),
        content: t('forms.settings.publicConfirmBody', {
          defaultValue: 'Anyone with the public link can view and submit this form without signing in.',
        }),
        okText: t('forms.settings.publicConfirmOk', { defaultValue: 'Make public' }),
        cancelText: t('common.cancel', { defaultValue: 'Cancel' }),
        onOk: apply,
      });
    } else {
      apply();
    }
  }

  // Populate form when template data changes
  const templateHash = `${template.id}-${template.requiresConsent}-${template.consentText}-${template.approvalWorkflowDefinitionId}`;
  useEffect(() => {
    form.setFieldsValue({
      requiresConsent: template.requiresConsent ?? false,
      consentText: template.consentText ?? '',
      approvalWorkflowDefinitionId: template.approvalWorkflowDefinitionId ?? undefined,
    });
  }, [templateHash, form]);

  // Build workflow options from API response — handle both paginated and flat array shapes
  const workflowItems: Array<{ id: string; name: string }> =
    Array.isArray(workflowData)
      ? workflowData
      : (workflowData as { items?: Array<{ id: string; name: string }> } | undefined)?.items ?? [];

  const workflowOptions = workflowItems.map((w) => ({ value: w.id, label: w.name }));

  async function handleSave() {
    try {
      const values = await form.validateFields();
      updateTemplate(
        {
          id: template.id,
          name: template.name,
          nameVi: template.nameVi ?? template.name,
          requiresConsent: values.requiresConsent ?? false,
          consentText: values.requiresConsent ? (values.consentText || null) : null,
          approvalWorkflowDefinitionId: values.approvalWorkflowDefinitionId || null,
          clearApprovalWorkflow: !values.approvalWorkflowDefinitionId,
        },
        {
          onSuccess: () => appMessage.success(t('forms.settings.saveSuccess', { defaultValue: 'Settings saved' })),
          onError: () => appMessage.error(t('forms.settings.saveError', { defaultValue: 'Failed to save settings' })),
        }
      );
    } catch {
      // Ant Design inline validation — errors shown in form items
    }
  }

  return (
    <div style={{ maxWidth: 600, paddingTop: 8 }}>
      <Title level={5} style={{ marginBottom: 4 }}>
        {t('forms.settings.title', { defaultValue: 'Settings' })}
      </Title>
      <Text type="secondary" style={{ fontSize: 12 }}>
        {t('forms.settings.subtitle', { defaultValue: 'Configure consent and approval workflow for this form.' })}
      </Text>

      <Divider style={{ margin: '16px 0' }} />

      {/* Public access section — saves immediately (separate PATCH endpoint), not batched with Save */}
      <Title level={5} style={{ marginBottom: 12 }}>
        {t('forms.settings.publicSection', { defaultValue: 'Public access' })}
      </Title>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Text>{t('forms.settings.publicAccess', { defaultValue: 'Allow anonymous public access' })}</Text>
        <Switch
          checked={template.isPublic ?? false}
          loading={isTogglingPublic}
          onChange={handlePublicToggle}
        />
      </div>
      <Text type="secondary" style={{ fontSize: 12 }}>
        {t('forms.settings.publicAccessHint', {
          defaultValue: 'Anyone with the public link can view and submit this form without signing in.',
        })}
      </Text>

      <Divider style={{ margin: '16px 0' }} />

      {/* PII + consent mismatch warning */}
      {hasPiiFields && !requiresConsent && (
        <Alert
          type="warning"
          showIcon
          message={t('forms.settings.piiWithoutConsent', {
            defaultValue: 'This form has PII fields but consent is not enabled. Enable consent for PDPL compliance.',
          })}
          style={{ marginBottom: 16 }}
        />
      )}

      <Form form={form} layout="vertical" size="small">
        {/* PDPL Consent section */}
        <Title level={5} style={{ marginBottom: 12 }}>
          {t('forms.settings.consentSection', { defaultValue: 'PDPL Consent' })}
        </Title>

        <Form.Item
          name="requiresConsent"
          label={t('forms.settings.requiresConsent', { defaultValue: 'Require consent before submission' })}
          valuePropName="checked"
        >
          <Switch />
        </Form.Item>

        {/* Consent text — only visible when requiresConsent is on */}
        {requiresConsent && (
          <Form.Item
            name="consentText"
            label={t('forms.settings.consentText', { defaultValue: 'Consent text' })}
            rules={[
              {
                max: 2000,
                message: t('forms.settings.consentTextMaxLength', { defaultValue: 'Consent text must not exceed 2000 characters' }),
              },
            ]}
          >
            <TextArea
              rows={5}
              maxLength={2000}
              showCount
              placeholder={t('forms.settings.consentTextPlaceholder', {
                defaultValue: 'Enter the consent text users must agree to before submitting...',
              })}
            />
          </Form.Item>
        )}

        <Divider style={{ margin: '16px 0' }} />

        {/* Approval workflow section */}
        <Title level={5} style={{ marginBottom: 12 }}>
          {t('forms.settings.workflowSection', { defaultValue: 'Approval Workflow' })}
        </Title>

        <Form.Item
          name="approvalWorkflowDefinitionId"
          label={t('forms.settings.approvalWorkflow', { defaultValue: 'Approval workflow' })}
          help={t('forms.settings.approvalWorkflowHint', { defaultValue: 'Leave empty to skip approval.' })}
        >
          <Select
            allowClear
            loading={isLoadingWorkflows}
            placeholder={t('forms.settings.workflowPlaceholder', { defaultValue: 'Select a workflow (optional)' })}
            options={workflowOptions}
          />
        </Form.Item>
      </Form>

      <Space style={{ marginTop: 16 }}>
        <Button type="primary" loading={isPending} onClick={handleSave}>
          {t('common.save', { defaultValue: 'Save' })}
        </Button>
        <Button
          onClick={() =>
            form.setFieldsValue({
              requiresConsent: template.requiresConsent ?? false,
              consentText: template.consentText ?? '',
              approvalWorkflowDefinitionId: template.approvalWorkflowDefinitionId ?? undefined,
            })
          }
        >
          {t('common.cancel', { defaultValue: 'Cancel' })}
        </Button>
      </Space>
    </div>
  );
}
