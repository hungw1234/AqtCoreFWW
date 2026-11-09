import { useState } from 'react';
import {
  Table,
  Button,
  Tag,
  Modal,
  Form,
  Input,
  Drawer,
  Descriptions,
  Select,
} from 'antd';
import { PlusOutlined, EyeOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import {
  useMessageLogs, useMessageLog, useLogMessage, usePartners,
  type MessageLogDto, type MessageDirection, type MessageLogStatus,
  type CreateMessageLogDto,
} from './integration-api';
import { appMessage } from '@/core/api/notification-bridge';

const DIRECTION_COLOR: Record<MessageDirection, string> = {
  Inbound: 'blue',
  Outbound: 'orange',
};

const STATUS_COLOR: Record<MessageLogStatus, string> = {
  Sent: 'processing',
  Delivered: 'green',
  Failed: 'red',
  Acknowledged: 'cyan',
};

// ─── Detail drawer ────────────────────────────────────────────────────────────

function MessageLogDetailDrawer({ logId, open, onClose }: {
  logId: string; open: boolean; onClose: () => void;
}) {
  const { t } = useTranslation();
  const { data, isLoading } = useMessageLog(logId);

  function formatPayload(raw?: string): string {
    if (!raw) return '—';
    try { return JSON.stringify(JSON.parse(raw), null, 2); } catch { return raw; }
  }

  return (
    <Drawer title={data?.messageType ?? t('integration.messageLogs.detailTitle')} open={open} onClose={onClose} width={560}>
      {!isLoading && data && (
        <Descriptions column={1} bordered size="small">
          <Descriptions.Item label={t('integration.messageLogs.col.partnerId')}>{data.partnerId}</Descriptions.Item>
          <Descriptions.Item label={t('integration.messageLogs.col.contractId')}>{data.contractId ?? '—'}</Descriptions.Item>
          <Descriptions.Item label={t('integration.messageLogs.col.direction')}>
            <Tag color={DIRECTION_COLOR[data.direction]}>{data.direction}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('integration.messageLogs.col.status')}>
            <Tag color={STATUS_COLOR[data.status]}>{data.status}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('integration.messageLogs.col.correlationId')}>{data.correlationId ?? '—'}</Descriptions.Item>
          <Descriptions.Item label={t('integration.messageLogs.col.sentAt')}>
            {dayjs(data.sentAt).format('DD/MM/YYYY HH:mm:ss')}
          </Descriptions.Item>
          <Descriptions.Item label={t('integration.messageLogs.col.acknowledgedAt')}>
            {data.acknowledgedAt ? dayjs(data.acknowledgedAt).format('DD/MM/YYYY HH:mm:ss') : '—'}
          </Descriptions.Item>
          <Descriptions.Item label={t('integration.messageLogs.col.payload')}>
            <pre style={{ margin: 0, fontSize: 12, maxHeight: 200, overflow: 'auto', whiteSpace: 'pre-wrap' }}>
              {formatPayload(data.payload)}
            </pre>
          </Descriptions.Item>
        </Descriptions>
      )}
    </Drawer>
  );
}

// ─── Log message modal ────────────────────────────────────────────────────────

function LogMessageModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const [form] = Form.useForm<CreateMessageLogDto>();
  const logMutation = useLogMessage();
  const { data: partnersData } = usePartners({ pageSize: 100 });
  const partnerOptions = (partnersData?.items ?? []).map((p) => ({ value: p.id, label: p.name }));

  const directionOptions = [
    { value: 'Inbound', label: t('integration.messageLogs.direction.inbound') },
    { value: 'Outbound', label: t('integration.messageLogs.direction.outbound') },
  ];

  async function handleSave() {
    const values = await form.validateFields();
    try {
      await logMutation.mutateAsync(values);
      appMessage.success(t('common.success'));
      form.resetFields();
      onClose();
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  return (
    <Modal
      title={t('integration.messageLogs.createTitle')}
      open={open}
      onOk={handleSave}
      onCancel={() => { form.resetFields(); onClose(); }}
      confirmLoading={logMutation.isPending}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
        <Form.Item name="partnerPublicId" label={t('integration.messageLogs.col.partnerId')} rules={[{ required: true }]}>
          <Select options={partnerOptions} showSearch optionFilterProp="label"
            placeholder={t('integration.contracts.selectPartner')} />
        </Form.Item>
        <Form.Item name="contractPublicId" label={t('integration.messageLogs.col.contractId')}>
          <Input placeholder={t('integration.messageLogs.contractIdPlaceholder')} />
        </Form.Item>
        <Form.Item name="direction" label={t('integration.messageLogs.col.direction')} rules={[{ required: true }]}>
          <Select options={directionOptions} />
        </Form.Item>
        <Form.Item name="messageType" label={t('integration.messageLogs.col.messageType')} rules={[{ required: true }]}>
          <Input placeholder="e.g. PATIENT_REFERRAL" />
        </Form.Item>
        <Form.Item name="correlationId" label={t('integration.messageLogs.col.correlationId')}>
          <Input />
        </Form.Item>
        <Form.Item name="payload" label={t('integration.messageLogs.col.payload')}>
          <Input.TextArea rows={4} placeholder='{"key": "value"}' />
        </Form.Item>
      </Form>
    </Modal>
  );
}

// ─── List page ────────────────────────────────────────────────────────────────

// MessageLogListPage — integration message log viewer with direction/status badges (no delete — audit trail)
export function MessageLogListPage() {
  const { t } = useTranslation();
  const [logOpen, setLogOpen] = useState(false);
  const [detailId, setDetailId] = useState<string | null>(null);
  const { antPagination, toQueryParams } = useServerPagination(20);

  const { data, isFetching } = useMessageLogs(toQueryParams());

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  const columns: ColumnsType<MessageLogDto> = [
    { title: t('integration.messageLogs.col.messageType'), dataIndex: 'messageType', key: 'messageType', ellipsis: true },
    {
      title: t('integration.messageLogs.col.direction'), dataIndex: 'direction', key: 'direction', width: 110,
      render: (v: MessageDirection) => <Tag color={DIRECTION_COLOR[v]}>{v}</Tag>,
    },
    {
      title: t('integration.messageLogs.col.status'), dataIndex: 'status', key: 'status', width: 120,
      render: (v: MessageLogStatus) => <Tag color={STATUS_COLOR[v]}>{v}</Tag>,
    },
    { title: t('integration.messageLogs.col.partnerId'), dataIndex: 'partnerId', key: 'partnerId', ellipsis: true, width: 200 },
    {
      title: t('integration.messageLogs.col.sentAt'), dataIndex: 'sentAt', key: 'sentAt', width: 150,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
    { title: t('integration.messageLogs.col.correlationId'), dataIndex: 'correlationId', key: 'correlationId', ellipsis: true, width: 180 },
    {
      title: '', key: 'actions', width: 60,
      render: (_, record) => (
        <Button size="small" icon={<EyeOutlined />} onClick={() => setDetailId(record.id)} />
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('integration.messageLogs.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setLogOpen(true)}>
            {t('integration.messageLogs.logMessage')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <Table<MessageLogDto>
          rowKey="id"
          columns={columns}
          dataSource={items}
          loading={isFetching}
          size="small"
          scroll={{ x: 900 }}
          pagination={{ ...antPagination, total }}
        />
      </AdminContentCard>

      <LogMessageModal open={logOpen} onClose={() => setLogOpen(false)} />

      {detailId && (
        <MessageLogDetailDrawer
          logId={detailId}
          open={Boolean(detailId)}
          onClose={() => setDetailId(null)}
        />
      )}
    </div>
  );
}
