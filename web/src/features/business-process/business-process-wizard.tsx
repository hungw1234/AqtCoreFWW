// business-process-wizard.tsx — 2-step create wizard: basics → workflow+intake → submit
import { useState } from 'react';
import { Steps, Button, Space, Alert, Spin } from 'antd';
import { Form } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { usePermissions } from '@/features/auth/use-permissions';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { BpBasicsStep } from './bp-basics-step';
import { BpWorkflowIntakeStep } from './bp-workflow-intake-step';
import { useCreateBp, getGetApiV1BusinessProcessesQueryKey } from './business-process-admin-api';
import type { BpBasicsValues } from './bp-basics-step';
import type { BpWorkflowIntakeValues } from './bp-workflow-intake-step';
import { appMessage } from '@/core/api/notification-bridge';

type CreateResult = { id?: string; publicId?: string };

export function BusinessProcessWizard() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { hasPermission, isPermissionsLoading } = usePermissions();

  // All hooks must be declared before any early returns (Rules of Hooks)
  const [current, setCurrent] = useState(0);
  const [intakeSelected, setIntakeSelected] = useState(false);
  const [basicsForm] = Form.useForm<BpBasicsValues>();
  const [workflowForm] = Form.useForm<BpWorkflowIntakeValues>();

  const createMutation = useCreateBp();

  if (isPermissionsLoading) {
    return <Spin size="large" style={{ display: 'block', margin: '80px auto' }} />;
  }

  if (!hasPermission('BUSINESSPROCESS.BP.MANAGE')) {
    return (
      <Alert
        type="error"
        message={t('common.forbidden', { defaultValue: 'Bạn không có quyền truy cập trang này.' })}
        style={{ margin: 24 }}
      />
    );
  }

  const steps = [
    { title: t('page.businessProcessAdmin.stepBasics', { defaultValue: 'Thông tin cơ bản' }) },
    { title: t('page.businessProcessAdmin.stepWorkflow', { defaultValue: 'Luồng & Biểu mẫu' }) },
  ];

  async function handleNext() {
    try {
      await basicsForm.validateFields();
      setCurrent(1);
    } catch {
      // antd shows inline errors
    }
  }

  async function validateAllSteps(): Promise<boolean> {
    // Both forms must validate at submit time. Re-validating basics also forces step-1 errors
    // to re-surface if the user reached step 2 through a stale-state path (HMR mid-session,
    // back/forward race, etc.) where code/name were not actually filled.
    try {
      await basicsForm.validateFields();
    } catch {
      setCurrent(0);
      return false;
    }
    try {
      await workflowForm.validateFields();
    } catch {
      return false;
    }
    return true;
  }

  async function handleSubmit() {
    if (!(await validateAllSteps())) return;

    const basics = basicsForm.getFieldsValue();
    const workflow = workflowForm.getFieldsValue();

    createMutation.mutate(
      {
        data: {
          code: basics.code,
          name: basics.name,
          description: basics.description ?? null,
          workflowDefinitionPublicId: workflow.workflowDefinitionPublicId,
          intakeFormTemplatePublicId: workflow.intakeFormTemplatePublicId ?? null,
          intakeFormTemplateVersion: workflow.intakeFormTemplateVersion ?? null,
        },
      },
      {
        onSuccess: (result) => {
          void queryClient.invalidateQueries({ queryKey: getGetApiV1BusinessProcessesQueryKey() });
          const res = result as unknown as CreateResult;
          const id = res?.publicId ?? res?.id;
          appMessage.success(t('page.businessProcessAdmin.createSuccess', { defaultValue: 'Tạo quy trình thành công.' }));
          if (id) {
            void navigate({ to: '/admin/business-processes/$id', params: { id } });
          } else {
            void navigate({ to: '/admin/business-processes' });
          }
        },
        onError: (err: unknown) => {
          const status = (err as { status?: number })?.status;
          if (status === 409) {
            basicsForm.setFields([{
              name: 'code',
              errors: [t('page.businessProcessAdmin.codeDuplicate', { defaultValue: 'Mã quy trình đã tồn tại.' })],
            }]);
            setCurrent(0);
          } else {
            appMessage.error(t('page.businessProcessAdmin.createFailed', { defaultValue: 'Tạo quy trình thất bại.' }));
          }
        },
      },
    );
  }

  return (
    <>
      <AdminPageHeader
        title={t('page.businessProcessAdmin.createTitle', { defaultValue: 'Tạo quy trình nghiệp vụ mới' })}
      />
      <AdminContentCard>
      <Steps current={current} items={steps} style={{ marginBottom: 32 }} />

      {/* Keep both step Forms mounted across step transitions. Conditional unmount of
          antd Form components can lose field-store values in edge cases (HMR mid-session,
          back/forward race) — toggling display preserves both form instances + values. */}
      <div style={{ display: current === 0 ? 'block' : 'none' }}>
        <BpBasicsStep form={basicsForm} />
      </div>
      <div style={{ display: current === 1 ? 'block' : 'none' }}>
        <BpWorkflowIntakeStep
          form={workflowForm}
          intakeSelected={intakeSelected}
          onIntakeChange={setIntakeSelected}
        />
      </div>

      {createMutation.isError && current === 1 && (
        <Alert
          type="error"
          message={t('page.businessProcessAdmin.createFailed', { defaultValue: 'Tạo quy trình thất bại.' })}
          style={{ marginBottom: 16 }}
          showIcon
        />
      )}

      <Space style={{ marginTop: 24 }}>
        {current === 0 && (
          <Button type="primary" onClick={() => void handleNext()}>
            {t('common.next', { defaultValue: 'Tiếp theo' })}
          </Button>
        )}
        {current === 1 && (
          <>
            <Button onClick={() => setCurrent(0)}>
              {t('common.back', { defaultValue: 'Quay lại' })}
            </Button>
            <Button
              type="primary"
              onClick={() => void handleSubmit()}
              loading={createMutation.isPending}
            >
              {t('page.businessProcessAdmin.submitCreate', { defaultValue: 'Tạo quy trình' })}
            </Button>
          </>
        )}
        <Button onClick={() => void navigate({ to: '/admin/business-processes' })}>
          {t('common.cancel', { defaultValue: 'Huỷ' })}
        </Button>
      </Space>
      </AdminContentCard>
    </>
  );
}
