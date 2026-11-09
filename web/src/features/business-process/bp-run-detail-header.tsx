// bp-run-detail-header.tsx — metadata bar for a BP Run (run id, BP name, state, started-at)

import { Descriptions, Tag, Skeleton } from 'antd';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import type { WorkflowInstanceDto } from '@/features/workflow/workflow-types';
import type { BpDefinitionDto } from './business-process-run-api';

interface BpRunDetailHeaderProps {
  instance: WorkflowInstanceDto | undefined;
  bpDefinition: BpDefinitionDto | undefined;
  isLoading: boolean;
}

export function BpRunDetailHeader({ instance, bpDefinition, isLoading }: BpRunDetailHeaderProps) {
  const { t } = useTranslation();

  if (isLoading) {
    return <Skeleton active paragraph={{ rows: 2 }} style={{ marginBottom: 24 }} />;
  }

  if (!instance) return null;

  return (
    <Descriptions
      bordered
      size="small"
      column={{ xs: 1, sm: 2, md: 2 }}
      style={{ marginBottom: 24 }}
    >
      <Descriptions.Item label={t('page.businessProcessRun.runId', { defaultValue: 'Mã phiên' })}>
        <code style={{ fontSize: 12 }}>{instance.id}</code>
      </Descriptions.Item>

      <Descriptions.Item label={t('page.businessProcessRun.bpName', { defaultValue: 'Quy trình' })}>
        {bpDefinition?.name ?? '—'}
      </Descriptions.Item>

      <Descriptions.Item label={t('page.businessProcessRun.currentState', { defaultValue: 'Trạng thái hiện tại' })}>
        <Tag color="blue">{instance.currentStateId}</Tag>
      </Descriptions.Item>

      <Descriptions.Item label={t('page.businessProcessRun.startedAt', { defaultValue: 'Bắt đầu lúc' })}>
        {dayjs(instance.startedAt).format('DD/MM/YYYY HH:mm')}
      </Descriptions.Item>

      {instance.completedAt && (
        <Descriptions.Item label={t('page.businessProcessRun.completedAt', { defaultValue: 'Hoàn thành lúc' })}>
          {dayjs(instance.completedAt).format('DD/MM/YYYY HH:mm')}
        </Descriptions.Item>
      )}

      {instance.dueAt && (
        <Descriptions.Item label={t('page.businessProcessRun.dueAt', { defaultValue: 'Hạn chót' })}>
          {dayjs(instance.dueAt).format('DD/MM/YYYY HH:mm')}
        </Descriptions.Item>
      )}
    </Descriptions>
  );
}
