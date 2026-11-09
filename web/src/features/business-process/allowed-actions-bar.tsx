// allowed-actions-bar.tsx — action buttons from workflow allowed-transitions API
// Handles discriminated response: requiresForms → modal; success → refresh view.

import { useState } from 'react';
import { Space, Button, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useAvailableTransitions, useTasksByInstance } from '@/features/workflow/workflow-api';
import { useAuth } from '@/features/auth';
import { useExecuteBpTransition } from './business-process-run-api';
import { TransitionConfirmModal } from './transition-confirm-modal';
import { parseSnapshotSchema } from '@/features/forms/form-snapshot-adapter';
import type { BpTransitionFormWithSnapshot, BpTransitionResponse } from './business-process-run-api';
import type { WorkflowTransitionDto } from '@/features/workflow/workflow-types';
import { appMessage } from '@/core/api/notification-bridge';
import { extractApiErrorMessage } from '@/core/api';

interface AllowedActionsBarProps {
  workflowInstanceId: string;
  bpPublicId: string;
  onTransitionSuccess: () => void;
}

interface PendingTransition {
  actionName: string;
  transitionForms: BpTransitionFormWithSnapshot[];
}

export function AllowedActionsBar({ workflowInstanceId, bpPublicId, onTransitionSuccess }: AllowedActionsBarProps) {
  const { t } = useTranslation();
  const [pendingTransition, setPendingTransition] = useState<PendingTransition | null>(null);

  const { data: transitions = [], isLoading } = useAvailableTransitions(workflowInstanceId);
  const { data: tasks, isLoading: tasksLoading } = useTasksByInstance(workflowInstanceId);
  const { user } = useAuth();
  const executeMutation = useExecuteBpTransition();

  // Ownership gate (mirrors BE ExecuteTransition): a work item claimed by another user may
  // only be advanced by its assignee — executing here would 409. Hide the actions and explain,
  // rather than letting the user dead-click into an error. Unclaimed or self-claimed → show
  // actions (clicking auto-claims an unclaimed task BE-side).
  const currentUserId = (user?.profile?.sub ?? '').toLowerCase();
  const blockingTask = (tasks ?? []).find(
    (tk) =>
      (tk.status === 'Open' || tk.status === 'InProgress') &&
      !!tk.assigneeId &&
      tk.assigneeId.toLowerCase() !== currentUserId,
  );

  async function handleTransitionClick(tr: WorkflowTransitionDto) {
    // Attempt transition without form data first — BE responds discriminated
    try {
      const result = await executeMutation.mutateAsync({
        workflowInstanceId,
        data: {
          bpPublicId,
          actionName: tr.actionName,
          transitionFormTemplateId: null,
          transitionFormData: null,
          consentGiven: false,
          comment: null,
        },
      }) as unknown as BpTransitionResponse;

      if (result && 'requiresForms' in result && result.requiresForms) {
        // BE returns the published snapshot inline; decode schemaJson → fields here so the modal
        // receives renderer-ready FormFieldDto[] (same adapter as intake / state forms).
        setPendingTransition({
          actionName: tr.actionName,
          transitionForms: (result.transitionForms ?? []).map((tf): BpTransitionFormWithSnapshot => ({
            transitionFormTemplateId: tf.transitionFormTemplateId,
            formTemplatePublicId: tf.formTemplatePublicId,
            fields: parseSnapshotSchema(tf.schemaJson),
          })),
        });
        return;
      }

      // Success branch — refresh detail view
      appMessage.success(t('page.businessProcessRun.transitionSuccess', { defaultValue: 'Chuyển trạng thái thành công.' }));
      onTransitionSuccess();
    } catch (err) {
      // Surface the backend reason (invalid transition, required forms, validation) instead of a
      // generic toast that hides which guard rejected; fall back to the generic message otherwise.
      appMessage.error(
        extractApiErrorMessage(err) ??
          t('page.businessProcessRun.transitionFailed', { defaultValue: 'Chuyển trạng thái thất bại.' }),
      );
    }
  }

  // Hold the spinner until transitions, the task list, AND the current user id are all known —
  // otherwise a foreign-claim check against an empty userId (or an unresolved task list) would
  // briefly render the wrong affordance (flash of buttons, or self-claimed read as foreign).
  if (isLoading || tasksLoading || !currentUserId) {
    return <Spin size="small" />;
  }

  if (blockingTask) {
    return (
      <Typography.Text type="secondary" style={{ fontSize: 13 }}>
        {t('page.businessProcessRun.claimedByOther', {
          defaultValue: 'Đã giao cho {{name}} — chỉ người nhận việc mới thao tác được.',
          name: blockingTask.assigneeName ?? t('page.businessProcessRun.someoneElse', { defaultValue: 'người khác' }),
        })}
      </Typography.Text>
    );
  }

  if (transitions.length === 0) {
    return (
      <span style={{ color: '#888', fontSize: 13 }}>
        {t('page.businessProcessRun.noActions', { defaultValue: 'Không có thao tác khả dụng.' })}
      </span>
    );
  }

  return (
    <>
      <Space wrap>
        {transitions.map((tr) => (
          <Button
            key={tr.id}
            type="primary"
            onClick={() => handleTransitionClick(tr)}
            loading={executeMutation.isPending}
          >
            {tr.actionLabelVi || tr.actionLabelEn || tr.actionName}
          </Button>
        ))}
      </Space>

      {pendingTransition && (
        <TransitionConfirmModal
          open={!!pendingTransition}
          workflowInstanceId={workflowInstanceId}
          bpPublicId={bpPublicId}
          actionName={pendingTransition.actionName}
          transitionForms={pendingTransition.transitionForms}
          onSuccess={() => {
            setPendingTransition(null);
            onTransitionSuccess();
          }}
          onCancel={() => setPendingTransition(null)}
        />
      )}
    </>
  );
}
