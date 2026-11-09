import { useState } from 'react';
import { Table, Tag, Typography, Button, Space, Select, Tooltip } from 'antd';
import { ReloadOutlined, SendOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useWebhookSubscriptions,
  useWebhookDeliveries,
  useTestWebhook,
  type WebhookDeliveryDto,
  type WebhookSubscriptionDto,
} from './webhook-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

// Webhook delivery logs admin page — select subscription, view delivery attempts
export function WebhookDeliveriesPage() {
  const { t } = useTranslation();
  const [selectedSubId, setSelectedSubId] = useState<string>('');
  const [page, setPage] = useState(1);

  const { data: subscriptions, isLoading: subsLoading } = useWebhookSubscriptions();
  const { data: deliveries, isFetching, refetch } = useWebhookDeliveries(selectedSubId, page);
  const testWebhook = useTestWebhook();

  const deliveryColumns: ColumnsType<WebhookDeliveryDto> = [
    {
      title: t('webhooks.col.eventType'),
      dataIndex: 'eventType',
      key: 'eventType',
      width: 160,
      ellipsis: true,
    },
    {
      title: t('webhooks.col.attempt'),
      dataIndex: 'attemptNumber',
      key: 'attemptNumber',
      width: 80,
      render: (v: number) => <Tag>{`#${v}`}</Tag>,
    },
    {
      title: t('webhooks.col.status'),
      dataIndex: 'isSuccess',
      key: 'isSuccess',
      width: 100,
      render: (v: boolean, record) => (
        <Tag color={v ? 'green' : 'red'}>
          {v ? 'OK' : `Fail${record.statusCode ? ` (${record.statusCode})` : ''}`}
        </Tag>
      ),
    },
    {
      title: t('webhooks.col.statusCode'),
      dataIndex: 'statusCode',
      key: 'statusCode',
      width: 80,
      render: (v: number | null) => v ?? '—',
    },
    {
      title: t('webhooks.col.error'),
      dataIndex: 'errorMessage',
      key: 'errorMessage',
      ellipsis: true,
      responsive: ['md'] as const,
      render: (v: string | null) => v ? <Text type="danger" style={{ fontSize: 12 }}>{v}</Text> : '—',
    },
    {
      title: t('webhooks.col.attemptedAt'),
      dataIndex: 'attemptedAt',
      key: 'attemptedAt',
      width: 160,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm:ss'),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('webhooks.title')}
        description={t('nav.webhooksDesc')}
        actions={
          selectedSubId ? (
            <Space>
              <Tooltip title={t('webhooks.refresh')}>
                <Button icon={<ReloadOutlined />} onClick={() => refetch()} />
              </Tooltip>
              <Tooltip title={t('webhooks.sendTest')}>
                <Button
                  icon={<SendOutlined />}
                  loading={testWebhook.isPending}
                  onClick={() => testWebhook.mutate(selectedSubId)}
                >
                  {t('webhooks.sendTest')}
                </Button>
              </Tooltip>
            </Space>
          ) : undefined
        }
      />
      <AdminContentCard noPadding>
        {/* Subscription selector toolbar */}
        <div style={{ padding: '16px 24px', borderBottom: '1px solid var(--gov-border)' }}>
          <Select
            placeholder={t('webhooks.selectSubscription')}
            style={{ width: 400 }}
            loading={subsLoading}
            value={selectedSubId || undefined}
            onChange={(v) => { setSelectedSubId(v); setPage(1); }}
            options={(subscriptions ?? []).map((s: WebhookSubscriptionDto) => ({
              label: `${s.endpointUrl} (${s.eventTypes.join(', ')})`,
              value: s.id,
            }))}
          />
        </div>

        {/* Delivery attempts table */}
        {selectedSubId ? (
          <Table<WebhookDeliveryDto>
            rowKey="id"
            columns={deliveryColumns}
            dataSource={deliveries ?? []}
            loading={isFetching}
            size="small"
            scroll={{ x: 700 }}
  
            pagination={{
              current: page,
              pageSize: 20,
              onChange: setPage,
              showSizeChanger: false,
            }}
            locale={{ emptyText: t('webhooks.noDeliveries') }}
          />
        ) : (
          <div style={{ padding: '24px' }}>
            <Text type="secondary">{t('webhooks.selectPrompt')}</Text>
          </div>
        )}
      </AdminContentCard>
    </div>
  );
}
