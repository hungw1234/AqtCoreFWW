// transition-confirm-modal.tsx — Modal wrapping form tabs for transition confirm forms
// Opens when transition POST returns requiresForms=true; submits with confirmFormData.
// H1 fix: refs via useImperativeHandle + Map<publicId, handle> — no hidden-button DOM hack
// M5 fix: useEffect sets prefill values after load (no initialValues double-set)

import { useRef, useEffect, useImperativeHandle, forwardRef } from 'react';
import { Modal, Tabs, Form, Button, Space, Skeleton, Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import { renderInteractiveField } from '@/features/forms/builder/form-field-renderer';
import { buildValidationRules, buildRequiredIfRules } from '@/features/forms/form-step-utils';
import { isFieldVisible } from '@/features/forms/form-visibility-helpers';
import { cleanFormData, extractSignaturePayload } from '@/features/forms/form-submission-helpers';
import { usePrefillContext } from './use-prefill-context';
import { useExecuteBpTransition } from './business-process-run-api';
import { useSigningProviderInfo } from '@/features/digital-signature/signature-api';
import type { BpTransitionFormWithSnapshot } from './business-process-run-api';
import type { FormFieldDto, SignaturePayloadRequest } from '@/features/forms/form-types';
import { appMessage } from '@/core/api/notification-bridge';

// ─── Types ─────────────────────────────────────────────────────────────────────

export interface TransitionConfirmModalProps {
  open: boolean;
  workflowInstanceId: string;
  bpPublicId: string;
  actionName: string;
  transitionForms: BpTransitionFormWithSnapshot[];
  onSuccess: () => void;
  onCancel: () => void;
}

/** Handle exposed by each TransitionFormPanel via useImperativeHandle */
export interface TransitionFormPanelHandle {
  /** Validates the antd Form; rejects with antd errorInfo on failure */
  validate(): Promise<void>;
  /** Returns cleaned form data after validation */
  getData(): Record<string, unknown>;
  /** Returns the captured digital-signature proof bundle after validation, or null if none */
  getSignature(): SignaturePayloadRequest | null;
}

interface TransitionFormPanelProps {
  form_def: BpTransitionFormWithSnapshot;
  workflowInstanceId: string;
}

// ─── Individual form panel (isolated Form.useForm, exposes imperative handle) ──

// forwardRef allows parent to pass a ref and call validate()/getData()
const TransitionFormPanel = forwardRef<TransitionFormPanelHandle, TransitionFormPanelProps>(
  function TransitionFormPanel({ form_def, workflowInstanceId }, ref) {
    const { t } = useTranslation();
    const [form] = Form.useForm<Record<string, unknown>>();
    const formValues = (Form.useWatch([], form) ?? {}) as Record<string, unknown>;
    // Snapshot cleaned data + captured signature after last successful validate() for getData()/getSignature()
    const lastCleanedRef = useRef<Record<string, unknown>>({});
    const lastSignatureRef = useRef<SignaturePayloadRequest | null>(null);

    // Resolve the tenant's signing provider only when this transition form needs it.
    const hasDigitalSignature = form_def.fields.some((f) => f.isActive && f.type === 'DigitalSignature');
    const { data: signingProvider } = useSigningProviderInfo(hasDigitalSignature);

    const { data: prefill, isLoading: prefillLoading } = usePrefillContext(
      workflowInstanceId,
      form_def.formTemplatePublicId,
    );

    // M5: set prefill values via effect — avoids remount on background refetch
    // Cast: antd setFieldsValue expects {} | undefined per index, but prefill is Record<string,unknown>.
    // Values are field values from BE; unknown-but-not-null at runtime — cast is safe.
    useEffect(() => {
      if (prefill) {
        form.setFieldsValue(prefill as Record<string, NonNullable<unknown> | undefined>);
      }
    }, [prefill, form]);

    // H1: expose validate + getData BEFORE any conditional return — hooks order stable
    useImperativeHandle(ref, () => ({
      async validate() {
        const values = await form.validateFields();
        lastCleanedRef.current = cleanFormData(values, form_def.fields, formValues);
        lastSignatureRef.current = extractSignaturePayload(values, form_def.fields);
      },
      getData() {
        return lastCleanedRef.current;
      },
      getSignature() {
        return lastSignatureRef.current;
      },
    }));

    const activeFields: FormFieldDto[] = [...form_def.fields]
      .filter((f) => f.isActive)
      .sort((a, b) => a.displayOrder - b.displayOrder);

    const visibleFields = activeFields.filter((f) =>
      isFieldVisible(f.visibilityRulesJson, formValues)
    );

    if (prefillLoading) {
      return <Skeleton active paragraph={{ rows: 3 }} style={{ marginTop: 16 }} />;
    }

    return (
      <Form
        form={form}
        layout="vertical"
        key={`${workflowInstanceId}-${form_def.formTemplatePublicId}-transition`}
        style={{ marginTop: 16 }}
      >
        {visibleFields.length === 0 && (
          <Alert
            type="info"
            message={t('page.businessProcessRun.noVisibleFields', { defaultValue: 'Không có trường nào hiển thị.' })}
            style={{ marginBottom: 16 }}
          />
        )}

        {visibleFields.map((field) =>
          renderInteractiveField({
            field,
            templateId: form_def.formTemplatePublicId,
            allFields: form_def.fields,
            signingProvider,
            extraRules: [
              ...buildValidationRules(field, t),
              ...buildRequiredIfRules(field, formValues, t),
            ],
            t,
          })
        )}
      </Form>
    );
  }
);

// ─── Modal orchestrator ───────────────────────────────────────────────────────

export function TransitionConfirmModal({
  open,
  workflowInstanceId,
  bpPublicId,
  actionName,
  transitionForms,
  onSuccess,
  onCancel,
}: TransitionConfirmModalProps) {
  const { t } = useTranslation();
  const executeMutation = useExecuteBpTransition();

  // H1: Map keyed by formTemplatePublicId — stable against tab mount/unmount order
  const formRefs = useRef<Map<string, TransitionFormPanelHandle>>(new Map());

  function setRef(publicId: string) {
    return (handle: TransitionFormPanelHandle | null) => {
      if (handle) {
        formRefs.current.set(publicId, handle);
      } else {
        formRefs.current.delete(publicId);
      }
    };
  }

  async function handleOk() {
    if (transitionForms.length === 0) {
      // No forms required — execute immediately
      executeMutation.mutate(
        {
          workflowInstanceId,
          data: {
            bpPublicId,
            actionName,
            transitionFormTemplateId: null,
            transitionFormData: {},
            consentGiven: false,
            comment: null,
          },
        },
        {
          onSuccess: () => {
            appMessage.success(t('page.businessProcessRun.transitionSuccess', { defaultValue: 'Chuyển trạng thái thành công.' }));
            onSuccess();
          },
          onError: () => {
            appMessage.error(t('page.businessProcessRun.transitionFailed', { defaultValue: 'Chuyển trạng thái thất bại.' }));
          },
        }
      );
      return;
    }

    // H1: sequential validate — first invalid tab surfaces field errors and halts
    for (const tf of transitionForms) {
      const handle = formRefs.current.get(tf.formTemplatePublicId);
      if (!handle) continue; // panel unmounted (tab not visited) — skip
      try {
        await handle.validate();
      } catch {
        // antd validateFields() highlights fields inline; no extra message needed
        return;
      }
    }

    // BE accepts single IReadOnlyDictionary<string, object?> per transition (P4 Slice 2C command)
    // Only the FIRST transition form data is submitted — multi-form is a BE limit, not FE bug
    const firstForm = transitionForms[0];
    const handle = firstForm ? formRefs.current.get(firstForm.formTemplatePublicId) : undefined;
    const firstData = handle ? handle.getData() : {};
    const firstSignature = handle ? handle.getSignature() : null;

    executeMutation.mutate(
      {
        workflowInstanceId,
        data: {
          bpPublicId,
          actionName,
          transitionFormTemplateId: firstForm?.transitionFormTemplateId ?? null,
          transitionFormData: firstData,
          consentGiven: false,
          comment: null,
          ...(firstSignature ? { signature: firstSignature } : {}),
        },
      },
      {
        onSuccess: () => {
          appMessage.success(t('page.businessProcessRun.transitionSuccess', { defaultValue: 'Chuyển trạng thái thành công.' }));
          formRefs.current.clear();
          onSuccess();
        },
        onError: () => {
          appMessage.error(t('page.businessProcessRun.transitionFailed', { defaultValue: 'Chuyển trạng thái thất bại.' }));
        },
      }
    );
  }

  const tabItems = transitionForms.map((tf) => ({
    key: tf.formTemplatePublicId,
    label: tf.formTemplatePublicId,
    children: (
      <TransitionFormPanel
        ref={setRef(tf.formTemplatePublicId)}
        form_def={tf}
        workflowInstanceId={workflowInstanceId}
      />
    ),
  }));

  return (
    <Modal
      open={open}
      title={t('page.businessProcessRun.confirmTransition', { defaultValue: 'Xác nhận chuyển trạng thái' })}
      onCancel={onCancel}
      footer={
        <Space>
          <Button onClick={onCancel}>
            {t('common.cancel', { defaultValue: 'Hủy' })}
          </Button>
          <Button
            type="primary"
            onClick={handleOk}
            loading={executeMutation.isPending}
          >
            {t('page.businessProcessRun.confirmSubmit', { defaultValue: 'Xác nhận' })}
          </Button>
        </Space>
      }
      width={700}
      destroyOnHidden
    >
      {transitionForms.length > 1 ? (
        <Tabs type="card" items={tabItems} />
      ) : (
        tabItems[0]?.children
      )}
    </Modal>
  );
}
