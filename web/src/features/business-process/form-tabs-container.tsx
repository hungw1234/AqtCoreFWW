// form-tabs-container.tsx — Antd Tabs with isolated Form per tab for BP Run state forms.
// P8-FU1: BE endpoint GET /business-processes/{bpId}/state-form-maps/{mapId}/schema now
// returns the pinned FormTemplate snapshot ({ formTemplatePublicId, versionNumber, schemaJson }).
// Field rendering + prefill + submit are wired here per tab. Each tab owns its Antd Form via
// Form.useForm() — required by React hooks rules so tabs cannot share form instances.

import { useMemo } from 'react';
import { Alert, Button, Form, Spin, Tabs } from 'antd';
import { useTranslation } from 'react-i18next';
import { renderInteractiveField } from '@/features/forms/builder/form-field-renderer';
import { buildValidationRules, buildRequiredIfRules } from '@/features/forms/form-step-utils';
import { isFieldVisible } from '@/features/forms/form-visibility-helpers';
import { cleanFormData, extractSignaturePayload } from '@/features/forms/form-submission-helpers';
import { parseSnapshotSchema } from '@/features/forms/form-snapshot-adapter';
import { useSigningProviderInfo } from '@/features/digital-signature/signature-api';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useBpStateFormSchema,
  useSubmitBpStateForm,
} from './business-process-run-api';
import { usePrefillContext } from './use-prefill-context';
import type { BpIntakeFormRawDto } from './business-process-run-api';
import type { FormFieldDto } from '@/features/forms/form-types';

// ─── Types ─────────────────────────────────────────────────────────────────────

export interface FormTabDefinition {
  /** PublicId (Guid) of the BusinessProcessStateForm map row — NOT the FormTemplate Guid. */
  formTemplatePublicId: string;
  /** Internal bigint id of the FormTemplate — used as path param for the submit POST endpoint. */
  formTemplateId: number | string;
  stateId: number | string;
  label: string;
}

interface FormTabsContainerProps {
  tabs: FormTabDefinition[];
  workflowInstanceId: string;
  bpPublicId: string;
  onSubmitSuccess?: () => void;
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function parseFields(raw: BpIntakeFormRawDto): FormFieldDto[] {
  return parseSnapshotSchema(raw.schemaJson);
}

// ─── Single isolated tab panel ─────────────────────────────────────────────────

interface FormTabPanelProps {
  tab: FormTabDefinition;
  workflowInstanceId: string;
  bpPublicId: string;
  onSubmitSuccess?: () => void;
}

function FormTabPanel({ tab, workflowInstanceId, bpPublicId, onSubmitSuccess }: FormTabPanelProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<Record<string, unknown>>();
  const formValues = (Form.useWatch([], form) ?? {}) as Record<string, unknown>;

  const schemaQuery = useBpStateFormSchema(bpPublicId, tab.formTemplatePublicId);
  const formTemplatePublicId = schemaQuery.data?.formTemplatePublicId ?? '';

  // Prefill is keyed on FormTemplate PublicId (not state-form-map id); enabled only after schema resolves.
  const prefill = usePrefillContext(workflowInstanceId, formTemplatePublicId);

  const allFields = useMemo<FormFieldDto[]>(
    () => (schemaQuery.data ? parseFields(schemaQuery.data) : []),
    [schemaQuery.data],
  );
  const activeFields = useMemo(
    () => [...allFields].filter((f) => f.isActive).sort((a, b) => a.displayOrder - b.displayOrder),
    [allFields],
  );
  const visibleFields = activeFields.filter((f) => isFieldVisible(f.visibilityRulesJson, formValues));

  // Resolve the tenant's signing provider only when this state form actually needs it.
  const hasDigitalSignature = allFields.some((f) => f.isActive && f.type === 'DigitalSignature');
  const { data: signingProvider } = useSigningProviderInfo(hasDigitalSignature);

  const submit = useSubmitBpStateForm();

  function handleFinish(values: Record<string, unknown>) {
    const cleaned = cleanFormData(values, allFields, formValues);
    const signature = extractSignaturePayload(values, allFields);
    submit.mutate(
      {
        workflowInstanceId,
        // safe: bigint IDENTITY << 2^53; convert to JS number for the path param (route expects long).
        stateId: Number(tab.stateId),
        data: {
          bpPublicId,
          formTemplateId: typeof tab.formTemplateId === 'string'
            ? tab.formTemplateId
            : Number(tab.formTemplateId),
          data: cleaned,
          consentGiven: true,
          ...(signature ? { signature } : {}),
        },
      },
      {
        onSuccess: () => {
          appMessage.success(t('page.businessProcessRun.stateFormSubmitted', { defaultValue: 'Đã lưu biểu mẫu trạng thái.' }));
          onSubmitSuccess?.();
        },
        onError: () => {
          appMessage.error(t('page.businessProcessRun.stateFormSubmitError', { defaultValue: 'Lưu biểu mẫu thất bại.' }));
        },
      },
    );
  }

  if (schemaQuery.isLoading) {
    return <Spin size="small" style={{ display: 'block', margin: '16px auto' }} />;
  }

  if (schemaQuery.isError || !schemaQuery.data) {
    return (
      <Alert
        type="warning"
        style={{ marginTop: 16 }}
        message={t('page.businessProcessRun.stateFormSchemaError', {
          defaultValue: 'Không tải được biểu mẫu trạng thái.',
        })}
      />
    );
  }

  if (visibleFields.length === 0) {
    return (
      <Alert
        type="info"
        style={{ marginTop: 16 }}
        message={t('page.businessProcessRun.stateFormEmpty', {
          defaultValue: 'Biểu mẫu trạng thái không có trường nào.',
        })}
      />
    );
  }

  return (
    <Form
      form={form}
      layout="vertical"
      onFinish={handleFinish}
      initialValues={prefill.data ?? {}}
    >
      {visibleFields.map((field) =>
        renderInteractiveField({
          field,
          templateId: formTemplatePublicId,
          allFields,
          signingProvider,
          extraRules: [
            ...buildValidationRules(field, t),
            ...buildRequiredIfRules(field, formValues, t),
          ],
          t,
        }),
      )}

      <Form.Item style={{ marginTop: 24 }}>
        <Button type="primary" htmlType="submit" loading={submit.isPending}>
          {t('page.businessProcessRun.stateFormSubmit', { defaultValue: 'Lưu biểu mẫu' })}
        </Button>
      </Form.Item>
    </Form>
  );
}

// ─── Container with Antd Tabs ─────────────────────────────────────────────────

export function FormTabsContainer({ tabs, workflowInstanceId, bpPublicId, onSubmitSuccess }: FormTabsContainerProps) {
  const { t } = useTranslation();

  if (tabs.length === 0) {
    return (
      <Alert
        type="info"
        message={t('page.businessProcessRun.noStateForms', { defaultValue: 'Trạng thái này không có biểu mẫu.' })}
      />
    );
  }

  const tabItems = tabs.map((tab) => ({
    key: tab.formTemplatePublicId,
    label: tab.label,
    children: (
      <FormTabPanel
        tab={tab}
        workflowInstanceId={workflowInstanceId}
        bpPublicId={bpPublicId}
        onSubmitSuccess={onSubmitSuccess}
      />
    ),
  }));

  return (
    <Tabs
      type="card"
      items={tabItems}
      destroyOnHidden={false}
    />
  );
}
