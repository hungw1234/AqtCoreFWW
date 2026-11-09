// new-bp-run-wizard.tsx — BP picker + optional intake form; submits POST /runs atomically
// On success → navigates to /business-process-runs/:workflowInstanceId

import { useState } from 'react';
import { Form, Select, Button, Alert, Spin, Divider } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import { renderInteractiveField } from '@/features/forms/builder/form-field-renderer';
import { buildValidationRules, buildRequiredIfRules } from '@/features/forms/form-step-utils';
import { isFieldVisible } from '@/features/forms/form-visibility-helpers';
import { cleanFormData, extractSignaturePayload } from '@/features/forms/form-submission-helpers';
import { parseSnapshotSchema } from '@/features/forms/form-snapshot-adapter';
import { useSigningProviderInfo } from '@/features/digital-signature/signature-api';
import { usePermissions } from '@/features/auth/use-permissions';
import { appMessage } from '@/core/api/notification-bridge';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import {
  useActiveBps,
  useBpIntakeForm,
  useStartBpRun,
} from './business-process-run-api';
import type { BpIntakeFormRawDto } from './business-process-run-api';
import type { FormFieldDto, SignaturePayloadRequest } from '@/features/forms/form-types';

// ─── Helpers ─────────────────────────────────────────────────────────────────

function parseIntakeFields(raw: BpIntakeFormRawDto): FormFieldDto[] {
  return parseSnapshotSchema(raw.schemaJson);
}

// ─── Intake form panel (isolated Form.useForm, receives pre-fetched data) ─────

interface IntakeFormPanelProps {
  intakeRaw: BpIntakeFormRawDto;
  onSubmit: (data: Record<string, unknown>, signature: SignaturePayloadRequest | null) => void;
  isPending: boolean;
}

function IntakeFormPanel({ intakeRaw, onSubmit, isPending }: IntakeFormPanelProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<Record<string, unknown>>();
  const formValues = (Form.useWatch([], form) ?? {}) as Record<string, unknown>;

  const allFields = parseIntakeFields(intakeRaw);
  const activeFields = [...allFields].filter((f) => f.isActive).sort((a, b) => a.displayOrder - b.displayOrder);
  const visibleFields = activeFields.filter((f) => isFieldVisible(f.visibilityRulesJson, formValues));

  // Resolve the tenant's signing provider only when the intake form actually needs it.
  const hasDigitalSignature = allFields.some((f) => f.isActive && f.type === 'DigitalSignature');
  const { data: signingProvider } = useSigningProviderInfo(hasDigitalSignature);

  function handleFinish(values: Record<string, unknown>) {
    const cleaned = cleanFormData(values, allFields, formValues);
    const signature = extractSignaturePayload(values, allFields);
    onSubmit(cleaned, signature);
  }

  return (
    <>
      <Divider>{t('page.businessProcessRun.intakeFormTitle', { defaultValue: 'Biểu mẫu tiếp nhận' })}</Divider>
      <Form form={form} layout="vertical" onFinish={handleFinish}>
        {visibleFields.map((field) =>
          renderInteractiveField({
            field,
            templateId: intakeRaw.formTemplatePublicId,
            allFields,
            signingProvider,
            extraRules: [
              ...buildValidationRules(field, t),
              ...buildRequiredIfRules(field, formValues, t),
            ],
            t,
          })
        )}

        <Form.Item style={{ marginTop: 24 }}>
          <Button type="primary" htmlType="submit" loading={isPending}>
            {t('page.businessProcessRun.startRun', { defaultValue: 'Bắt đầu quy trình' })}
          </Button>
        </Form.Item>
      </Form>
    </>
  );
}

// ─── Intake-or-direct-start (fetches, then branches) ─────────────────────────

interface IntakeOrDirectProps {
  bpId: string;
  onSubmit: (
    data: Record<string, unknown> | null,
    signature: SignaturePayloadRequest | null,
  ) => void;
  isPending: boolean;
}

function IntakeOrDirect({ bpId, onSubmit, isPending }: IntakeOrDirectProps) {
  const { t } = useTranslation();
  const { data: intakeRaw, isLoading, isError, status } = useBpIntakeForm(bpId, !!bpId);

  if (isLoading) {
    return <Spin size="small" style={{ display: 'block', margin: '16px auto' }} />;
  }

  if (isError) {
    return (
      <Alert
        type="warning"
        message={t('page.businessProcessRun.intakeFormError', { defaultValue: 'Không tải được biểu mẫu tiếp nhận.' })}
        style={{ marginBottom: 16 }}
      />
    );
  }

  // 204 branch — no intake form pinned; offer direct start
  if (status === 'success' && !intakeRaw) {
    return (
      <div style={{ marginTop: 16 }}>
        <Alert
          type="info"
          message={t('page.businessProcessRun.noIntakeForm', { defaultValue: 'Quy trình này không có biểu mẫu tiếp nhận.' })}
          style={{ marginBottom: 16 }}
          showIcon
        />
        <Button type="primary" loading={isPending} onClick={() => onSubmit(null, null)}>
          {t('page.businessProcessRun.startRun', { defaultValue: 'Bắt đầu quy trình' })}
        </Button>
      </div>
    );
  }

  if (!intakeRaw) return null;

  return (
    <IntakeFormPanel
      intakeRaw={intakeRaw}
      onSubmit={(data, signature) => onSubmit(data, signature)}
      isPending={isPending}
    />
  );
}

// ─── Main wizard ──────────────────────────────────────────────────────────────

export function NewBpRunWizard() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [selectedBpId, setSelectedBpId] = useState<string | null>(null);
  const { hasPermission, isPermissionsLoading } = usePermissions();

  const { data: activeBps, isLoading: bpsLoading } = useActiveBps();
  const startMutation = useStartBpRun();

  if (isPermissionsLoading) {
    return <Spin size="large" style={{ display: 'block', margin: '80px auto' }} />;
  }

  if (!hasPermission('BUSINESSPROCESS.BP.VIEW')) {
    return (
      <Alert
        type="error"
        message={t('common.forbidden', { defaultValue: 'Bạn không có quyền truy cập trang này.' })}
        style={{ margin: 24 }}
      />
    );
  }

  // BE /active returns PagedResult — read .items per the codebase paged-consumer
  // convention (?? [] = defense-in-depth: loading/undefined → empty picker, no crash).
  const bpOptions = (activeBps?.items ?? []).map((bp) => ({
    label: bp.name,
    value: bp.id,
  }));

  function handleStartWithData(
    intakeFormData: Record<string, unknown> | null,
    signature: SignaturePayloadRequest | null,
  ) {
    if (!selectedBpId) return;

    startMutation.mutate(
      {
        id: selectedBpId,
        // One token per submit attempt. React Query reuses these variables on a retry of the same
        // mutate → same token → AntiDoubleSubmit middleware returns 409 (no duplicate run). A separate
        // user-initiated submit mints a new token → succeeds (intentional multi-run preserved). A true
        // pre-isPending double-click mints two tokens; that race stays covered by the antd loading-disable.
        requestToken: crypto.randomUUID(),
        data: {
          entityType: 'BusinessProcess',
          entityId: selectedBpId as unknown as string,
          intakeFormData: intakeFormData ?? null,
          consentGiven: false,
          ...(signature ? { signature } : {}),
        },
      },
      {
        onSuccess: (result) => {
          // BE returns void in OpenAPI; actual response contains workflowInstanceId
          const res = result as unknown as { workflowInstanceId?: string; id?: string };
          const instanceId = res?.workflowInstanceId ?? res?.id;
          if (instanceId) {
            navigate({ to: '/business-process-runs/$id', params: { id: instanceId } });
          } else {
            appMessage.success(t('page.businessProcessRun.runStarted', { defaultValue: 'Đã khởi tạo quy trình.' }));
          }
        },
        onError: (error) => {
          // 409 = AntiDoubleSubmit rejected a re-fired identical submit; not a fatal error.
          // The axios interceptor rejects with a normalized flat error ({ status, code, ... }),
          // so read `status` directly — `error.response` is not present on the normalized shape.
          const status = (error as { status?: number })?.status;
          if (status === 409) {
            appMessage.warning(t('page.businessProcessRun.duplicateSubmit', {
              defaultValue: 'Yêu cầu đã được gửi, vui lòng không gửi lại.',
            }));
            return;
          }
          appMessage.error(t('page.businessProcessRun.startFailed', { defaultValue: 'Khởi tạo quy trình thất bại.' }));
        },
      }
    );
  }

  return (
    <>
      <AdminPageHeader
        title={t('page.businessProcessRun.newRunTitle', { defaultValue: 'Bắt đầu quy trình mới' })}
      />
      <AdminContentCard>
        <Form layout="vertical">
          <Form.Item
            label={t('page.businessProcessRun.selectBp', { defaultValue: 'Chọn quy trình' })}
            required
          >
            <Select
              placeholder={t('page.businessProcessRun.selectBpPlaceholder', { defaultValue: 'Chọn quy trình...' })}
              options={bpOptions}
              loading={bpsLoading}
              onChange={(val: string) => setSelectedBpId(val)}
              value={selectedBpId}
              showSearch
              optionFilterProp="label"
              style={{ width: '100%' }}
            />
          </Form.Item>
        </Form>

        {selectedBpId && (
          <IntakeOrDirect
            bpId={selectedBpId}
            onSubmit={handleStartWithData}
            isPending={startMutation.isPending}
          />
        )}
      </AdminContentCard>
    </>
  );
}
