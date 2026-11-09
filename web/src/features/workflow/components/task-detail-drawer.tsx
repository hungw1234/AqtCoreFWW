// Task detail drawer — shows task info with complete + reassign + delegate actions

import { useState } from 'react';
import { Drawer, Descriptions, Tag, Button, Space, Form, Input } from 'antd';
import { useTranslation } from 'react-i18next';
import { useAuth } from '@/features/auth';
import { useCompleteTask, useReassignTask, useReturnDelegation } from '../workflow-api';
import { DelegateTaskDialog } from '../workflow-delegate-task-dialog';
import type { WorkflowTaskDto, ReassignTaskRequest } from '../workflow-types';
import { appMessage } from '@/core/api/notification-bridge';

const STATUS_COLORS: Record<number, string> = { 0: 'blue', 1: 'orange', 2: 'green', 3: 'default' };
// Numeric task status/priority → i18n key suffix (the labels live under workflow.inbox.status/priority,
// shared with the inbox list so both surfaces render the same translated text).
const STATUS_LABEL_KEYS: Record<number, string> = { 0: 'open', 1: 'inProgress', 2: 'done', 3: 'cancelled' };
const PRIORITY_COLORS: Record<number, string> = { 0: 'default', 1: 'blue', 2: 'orange', 3: 'red' };
const PRIORITY_LABEL_KEYS: Record<number, string> = { 0: 'low', 1: 'normal', 2: 'high', 3: 'urgent' };

interface Props {
  task: WorkflowTaskDto | null;
  open: boolean;
  onClose: () => void;
}

export function TaskDetailDrawer({ task, open, onClose }: Props) {
  const { t } = useTranslation();
  const { user } = useAuth();
  const [reassigning, setReassigning] = useState(false);
  const [delegateDialogOpen, setDelegateDialogOpen] = useState(false);
  const [form] = Form.useForm<{ newAssigneeId: string; reason?: string }>();
  const completeMutation = useCompleteTask();
  const reassignMutation = useReassignTask();
  const returnDelegationMutation = useReturnDelegation();

  const handleReturnDelegation = async () => {
    if (!task) return;
    await returnDelegationMutation.mutateAsync(task.id);
    appMessage.success(t('workflow.delegation.returnSuccess'));
    onClose();
  };

  const handleComplete = async () => {
    if (!task) return;
    await completeMutation.mutateAsync(task.id);
    appMessage.success(t('workflow.inbox.completed'));
    onClose();
  };

  const handleReassign = async () => {
    if (!task) return;
    const values = await form.validateFields();
    const body: ReassignTaskRequest = {
      newAssigneeId: values.newAssigneeId,
      reason: values.reason,
    };
    await reassignMutation.mutateAsync({ taskId: task.id, body });
    appMessage.success(t('workflow.inbox.reassigned'));
    form.resetFields();
    setReassigning(false);
    onClose();
  };

  const isActionable = task?.status === 0 || task?.status === 1;
  const actorId = user?.profile?.sub ?? '';

  return (
    <>
    <Drawer
      title={task?.title ?? ''}
      open={open}
      onClose={() => { setReassigning(false); form.resetFields(); onClose(); }}
      width={520}
      footer={
        isActionable && (
          <Space wrap>
            <Button
              type="primary"
              loading={completeMutation.isPending}
              onClick={handleComplete}
              disabled={!actorId}
            >
              {t('workflow.inbox.complete')}
            </Button>
            <Button onClick={() => setReassigning(!reassigning)}>
              {t('workflow.inbox.reassign')}
            </Button>
            <Button onClick={() => setDelegateDialogOpen(true)}>
              {t('workflow.delegation.delegateBtn')}
            </Button>
            {task?.delegatedFromUserId && (
              <Button
                danger
                loading={returnDelegationMutation.isPending}
                onClick={handleReturnDelegation}
              >
                {t('workflow.delegation.returnBtn')}
              </Button>
            )}
          </Space>
        )
      }
    >
      {task && (
        <>
          <Descriptions column={1} size="small" bordered>
            {/* Labels use col.* (plain strings); status/priority themselves are objects of value
                labels, so t() on the bare key would return the key, not "Trạng thái"/"Ưu tiên". */}
            <Descriptions.Item label={t('workflow.inbox.col.status')}>
              <Tag color={STATUS_COLORS[task.status]}>{t(`workflow.inbox.status.${STATUS_LABEL_KEYS[task.status]}`)}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label={t('workflow.inbox.col.priority')}>
              <Tag color={PRIORITY_COLORS[task.priority]}>{t(`workflow.inbox.priority.${PRIORITY_LABEL_KEYS[task.priority]}`)}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label={t('workflow.inbox.assignee')}>{task.assigneeId ?? '—'}</Descriptions.Item>
            <Descriptions.Item label={t('workflow.inbox.dueDate')}>{task.dueDate ?? '—'}</Descriptions.Item>
            <Descriptions.Item label={t('workflow.inbox.created')}>{task.createdAt}</Descriptions.Item>
            {task.description && (
              <Descriptions.Item label={t('workflow.inbox.description')}>{task.description}</Descriptions.Item>
            )}
            {task.delegatedFromUserId && (
              <Descriptions.Item label={t('workflow.delegation.delegatedFrom')}>
                <Tag color="purple">{t('workflow.delegation.autoDelegatedBadge')}</Tag>
                {task.delegatedFromUserId}
              </Descriptions.Item>
            )}
          </Descriptions>

          {reassigning && (
            <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
              <Form.Item name="newAssigneeId" label={t('workflow.inbox.newAssignee')} rules={[{ required: true }]}>
                <Input />
              </Form.Item>
              <Form.Item name="reason" label={t('workflow.inbox.reason')}>
                <Input.TextArea rows={2} />
              </Form.Item>
              <Button type="primary" loading={reassignMutation.isPending} onClick={handleReassign}>
                {t('common.save')}
              </Button>
            </Form>
          )}
        </>
      )}
    </Drawer>
    {task && (
      <DelegateTaskDialog
        taskId={task.id}
        open={delegateDialogOpen}
        onClose={() => setDelegateDialogOpen(false)}
      />
    )}
    </>
  );
}
