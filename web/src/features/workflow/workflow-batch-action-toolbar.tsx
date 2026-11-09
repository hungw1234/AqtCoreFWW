// Batch action toolbar — shown when instances selected in monitoring dashboard

import { useState } from 'react';
import { Space, Button, Modal, Form, Input, Alert } from 'antd';
import { ThunderboltOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useBatchTransition } from './workflow-api';
import { appMessage } from '@/core/api/notification-bridge';
import type {
  BatchTransitionResult,
  BatchTransitionItem,
} from './workflow-types';
import { WorkflowBatchResultDialog } from './workflow-batch-result-dialog';

const MAX_BATCH = 50;
// Soft threshold — surface confirm dialog above this count to guard against misclicks.
// Hard cap (MAX_BATCH) disables the button entirely.
const CONFIRM_THRESHOLD = 20;

interface Props {
  selectedInstanceIds: string[];
  /** Called after batch submit succeeds — parent should clear selection state */
  onClearSelection: () => void;
}

/**
 * Shows batch action bar when 1+ instances are selected.
 * Admin enters transitionId + optional comment; batch fires via /batch-transition endpoint.
 * Partial-failure result surfaced via WorkflowBatchResultDialog.
 */
export function WorkflowBatchActionToolbar({ selectedInstanceIds, onClearSelection }: Props) {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm<{ transitionId: string; comment?: string }>();
  const [result, setResult] = useState<BatchTransitionResult | null>(null);

  const batchMutation = useBatchTransition();
  const count = selectedInstanceIds.length;
  const isOverLimit = count > MAX_BATCH;

  if (count === 0) return null;

  const handleActionClick = () => {
    if (count > CONFIRM_THRESHOLD) {
      Modal.confirm({
        title: t('workflow.batch.confirmTitle'),
        content: t('workflow.batch.confirmContent', { count }),
        okText: t('workflow.batch.confirmOk'),
        cancelText: t('common.cancel'),
        onOk: () => setModalOpen(true),
      });
      return;
    }
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      const items: BatchTransitionItem[] = selectedInstanceIds.map((instanceId) => ({
        instanceId,
        transitionId: values.transitionId,
      }));
      const batchResult = await batchMutation.mutateAsync({
        items,
        comment: values.comment || null,
      });
      setResult(batchResult);
      setModalOpen(false);
      form.resetFields();
      appMessage.success(t('workflow.batch.submitted', { count: batchResult.successCount }));
      // Clear selection so batch can't be re-fired on already-transitioned items
      onClearSelection();
    } catch {
      appMessage.error(t('common.error'));
    }
  };

  return (
    <>
      <div
        style={{
          padding: '8px 12px',
          background: '#e6f4ff',
          border: '1px solid #91caff',
          borderRadius: 4,
          marginBottom: 12,
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
        }}
      >
        <Space>
          <strong>{t('workflow.batch.selectedCount', { count })}</strong>
          {isOverLimit && (
            <span style={{ color: '#cf1322' }}>
              {t('workflow.batch.overLimit', { max: MAX_BATCH })}
            </span>
          )}
        </Space>
        <Space>
          <Button
            type="primary"
            icon={<ThunderboltOutlined />}
            disabled={isOverLimit}
            onClick={handleActionClick}
          >
            {t('workflow.batch.actionButton')}
          </Button>
          <Button onClick={onClearSelection}>{t('common.clear')}</Button>
        </Space>
      </div>

      <Modal
        title={t('workflow.batch.modalTitle', { count })}
        open={modalOpen}
        onOk={handleSubmit}
        onCancel={() => {
          setModalOpen(false);
          form.resetFields();
        }}
        confirmLoading={batchMutation.isPending}
        okText={t('workflow.batch.submit')}
        cancelText={t('common.cancel')}
        destroyOnHidden
      >
        <Alert
          style={{ marginBottom: 12 }}
          type="info"
          showIcon
          message={t('workflow.batch.modalHint')}
        />
        <Form form={form} layout="vertical">
          <Form.Item
            name="transitionId"
            label={t('workflow.batch.transitionId')}
            rules={[{ required: true }]}
            tooltip={t('workflow.batch.transitionIdHint')}
          >
            <Input placeholder="00000000-0000-0000-0000-000000000000" />
          </Form.Item>
          <Form.Item name="comment" label={t('workflow.batch.comment')}>
            <Input.TextArea rows={3} maxLength={2000} showCount />
          </Form.Item>
        </Form>
      </Modal>

      <WorkflowBatchResultDialog result={result} onClose={() => setResult(null)} />
    </>
  );
}
