// bp-run-detail-page.tsx — orchestrator for BP Run detail view
// bpId resolved from instance.entityId (entityType === "BusinessProcess" contract)

import { useCallback } from 'react';
import { Spin, Alert, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import { useWorkflowInstance, workflowKeys } from '@/features/workflow/workflow-api';
import { usePermissions } from '@/features/auth/use-permissions';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { TransitionTimeline } from '@/features/workflow/components/transition-timeline';
import { useBpDefinition, bpRunSubmissionsKey } from './business-process-run-api';
import { BpRunDetailHeader } from './bp-run-detail-header';
import { AllowedActionsBar } from './allowed-actions-bar';
import { NextHandlerCard } from './next-handler-card';
import { BpRunSubmissionsCard } from './bp-run-submissions-card';
import { FormTabsContainer } from './form-tabs-container';
import type { FormTabDefinition } from './form-tabs-container';

const { Title } = Typography;

interface BpRunDetailPageProps {
  workflowInstanceId: string;
}

export function BpRunDetailPage({ workflowInstanceId }: BpRunDetailPageProps) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const { hasPermission, isPermissionsLoading } = usePermissions();

  // All hooks must be called unconditionally — early returns come after
  const { data: instance, isLoading: instanceLoading } = useWorkflowInstance(workflowInstanceId);

  // bpId from entityId — only valid when entityType === "BusinessProcess"
  const bpId = instance?.entityType === 'BusinessProcess' ? instance.entityId : undefined;

  // Always call — disabled when bpId undefined to avoid empty-string API call
  const { data: bpDef, isLoading: bpLoading } = useBpDefinition(bpId ?? '');

  const handleTransitionSuccess = useCallback(() => {
    qc.invalidateQueries({ queryKey: workflowKeys.instance(workflowInstanceId) });
    qc.invalidateQueries({ queryKey: workflowKeys.availableTransitions(workflowInstanceId) });
    // A transition completes the source task and may spawn a new pool task at the target
    // state. NextHandlerCard / AllowedActionsBar both read tasksByInstance — without this the
    // card keeps rendering the just-completed task as still pending (stale "Nhận việc").
    qc.invalidateQueries({ queryKey: workflowKeys.tasksByInstance(workflowInstanceId) });
    // Both submit paths (transition + state-form) route here; the submissions card reads the data
    // they just changed, so refresh it too or it keeps showing the pre-submit snapshot.
    qc.invalidateQueries({ queryKey: bpRunSubmissionsKey(workflowInstanceId) });
  }, [qc, workflowInstanceId]);

  const isLoading = instanceLoading || (!!bpId && bpLoading);

  // ── Early returns (all hooks already called above) ──────────────────────────

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

  if (isLoading) {
    return <Spin size="large" style={{ display: 'block', margin: '80px auto' }} />;
  }

  if (!instance) {
    return (
      <Alert
        type="error"
        message={t('page.businessProcessRun.notFound', { defaultValue: 'Không tìm thấy phiên quy trình.' })}
        style={{ margin: 24 }}
      />
    );
  }

  // Guard: entityType mismatch — this page only handles BP runs
  if (!bpId) {
    return (
      <Alert
        type="error"
        message={t('page.businessProcessRun.entityTypeMismatch', {
          defaultValue: `Phiên này không phải quy trình BP (entityType: ${instance.entityType}).`,
        })}
        style={{ margin: 24 }}
      />
    );
  }

  // Build state form tabs for current state.
  // NOTE: BpStateFormMapDto carries bigint formTemplateId only — no FormFieldDto[] inline.
  // State form tabs render empty until a form-schema fetch is available (deferred to post-MVP).
  // Tab submit (POST state-forms) and prefill routing work when formTemplatePublicId is resolved.
  const stateForms: FormTabDefinition[] = (bpDef?.stateForms ?? [])
    .filter((sf) => String(sf.workflowStateId) === String(instance.currentStateId))
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map((sf) => ({
      formTemplatePublicId: sf.id,
      formTemplateId: sf.formTemplateId,
      stateId: sf.workflowStateId,
      // M3: i18n label — fallback 'Biểu mẫu #N' for non-vi locales
      label: t('page.businessProcessRun.formNumber', { defaultValue: 'Biểu mẫu #{{n}}', n: sf.displayOrder + 1 }),
    }));

  return (
    <>
      <AdminPageHeader
        title={t('page.businessProcessRun.detailTitle', { defaultValue: 'Chi tiết phiên quy trình' })}
      />
      <AdminContentCard>
        <BpRunDetailHeader
          instance={instance}
          bpDefinition={bpDef}
          isLoading={false}
        />
      </AdminContentCard>
      <div style={{ marginTop: 'var(--page-gap, 24px)' }}>
        <AdminContentCard>
          <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
            {t('page.businessProcessRun.allowedActions', { defaultValue: 'Thao tác' })}
          </Title>
          <AllowedActionsBar
            workflowInstanceId={workflowInstanceId}
            bpPublicId={bpId}
            onTransitionSuccess={handleTransitionSuccess}
          />
        </AdminContentCard>
      </div>
      <div style={{ marginTop: 'var(--page-gap, 24px)' }}>
        <AdminContentCard>
          <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
            {t('page.businessProcessRun.nextHandler.title', { defaultValue: 'Người xử lý tiếp theo' })}
          </Title>
          <NextHandlerCard workflowInstanceId={workflowInstanceId} />
        </AdminContentCard>
      </div>
      <div style={{ marginTop: 'var(--page-gap, 24px)' }}>
        <AdminContentCard>
          <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
            {t('page.businessProcessRun.submittedForms', { defaultValue: 'Dữ liệu biểu mẫu đã nhập' })}
          </Title>
          <BpRunSubmissionsCard workflowInstanceId={workflowInstanceId} />
        </AdminContentCard>
      </div>
      <div style={{ marginTop: 'var(--page-gap, 24px)' }}>
        <AdminContentCard>
          <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
            {t('page.businessProcessRun.stateForms', { defaultValue: 'Biểu mẫu trạng thái' })}
          </Title>
          <FormTabsContainer
            tabs={stateForms}
            workflowInstanceId={workflowInstanceId}
            bpPublicId={bpId}
            onSubmitSuccess={handleTransitionSuccess}
          />
        </AdminContentCard>
      </div>
      <div style={{ marginTop: 'var(--page-gap, 24px)' }}>
        <AdminContentCard>
          <Title level={5} style={{ marginTop: 0, marginBottom: 12 }}>
            {t('page.businessProcessRun.history', { defaultValue: 'Lịch sử xử lý' })}
          </Title>
          <TransitionTimeline history={instance.history} />
        </AdminContentCard>
      </div>
    </>
  );
}
