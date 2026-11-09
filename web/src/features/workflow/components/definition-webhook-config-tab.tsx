// Webhook config tab — manage webhook subscriptions for "workflow.transitioned" event type

import { useState } from 'react';
import { Table, Button, Tag, Popconfirm, Modal, Form, Input, Space, Tooltip, Flex, Typography, Empty } from 'antd';
import { PlusOutlined, DeleteOutlined, ThunderboltOutlined, LinkOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useWebhookSubscriptions,
  useTestWebhook,
  type WebhookSubscriptionDto,
} from '../../webhooks/webhook-api';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient, extractApiErrorMessage } from '@/core/api';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

const WORKFLOW_EVENT_TYPE = 'workflow.transitioned';

// Webhooks are tenant-scoped (not per-definition). This tab shows all
// workflow.transitioned subscriptions for the current tenant.

/** Creates a webhook subscription scoped to workflow.transitioned event type */
function useCreateWorkflowWebhook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { endpointUrl: string; secret?: string }) =>
      apiClient
        .post<{ data: WebhookSubscriptionDto }>('/admin/webhooks', {
          endpointUrl: body.endpointUrl,
          eventTypes: [WORKFLOW_EVENT_TYPE],
          secret: body.secret || undefined,
        })
        .then((r) => r.data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['webhooks'] }),
  });
}

/** Deletes a webhook subscription */
function useDeleteWebhook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete(`/admin/webhooks/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['webhooks'] }),
  });
}

export function DefinitionWebhookConfigTab() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm();

  const { data: allSubscriptions, isLoading } = useWebhookSubscriptions();
  const createMutation = useCreateWorkflowWebhook();
  const deleteMutation = useDeleteWebhook();
  const testMutation = useTestWebhook();

  // Filter to only workflow.transitioned subscriptions
  const webhookSubs = (allSubscriptions ?? []).filter((s) =>
    s.eventTypes.includes(WORKFLOW_EVENT_TYPE),
  );

  const handleCreate = async () => {
    // Validation rejects render inline on the form — return silently, no toast noise.
    let values: Awaited<ReturnType<typeof form.validateFields>>;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }
    try {
      await createMutation.mutateAsync(values);
      appMessage.success(t('common.saved'));
      setModalOpen(false);
      form.resetFields();
    } catch (err) {
      // Surface the server reason (e.g. duplicate URL, invalid endpoint) over a generic toast.
      appMessage.error(extractApiErrorMessage(err) ?? t('common.error'));
    }
  };

  const handleTest = async (id: string) => {
    try {
      await testMutation.mutateAsync(id);
      appMessage.success(t('workflow.webhookTestSent'));
    } catch (err) {
      appMessage.error(extractApiErrorMessage(err) ?? t('common.error'));
    }
  };

  const columns: ColumnsType<WebhookSubscriptionDto> = [
    {
      title: t('workflow.webhookUrl'),
      dataIndex: 'endpointUrl',
      ellipsis: true,
      render: (url: string) => (
        <Tooltip title={url}>
          <span>{url}</span>
        </Tooltip>
      ),
    },
    {
      title: t('workflow.webhookEvents'),
      dataIndex: 'eventTypes',
      width: 200,
      render: (types: string[]) =>
        types.map((et) => (
          <Tag key={et} color="blue">
            {et}
          </Tag>
        )),
    },
    {
      title: t('workflow.webhookStatus'),
      dataIndex: 'isActive',
      width: 80,
      render: (active: boolean) => (
        <Tag color={active ? 'green' : 'default'}>
          {active ? t('workflow.active') : t('workflow.inactive')}
        </Tag>
      ),
    },
    {
      title: t('common.actions'),
      width: 120,
      render: (_: unknown, r: WebhookSubscriptionDto) => (
        <Space size="small">
          <Tooltip title={t('workflow.webhookTest')}>
            <Button
              size="small"
              icon={<ThunderboltOutlined />}
              onClick={() => handleTest(r.id)}
              loading={testMutation.isPending}
            />
          </Tooltip>
          <Tooltip title={t('workflow.webhookDeliveries')}>
            <Button
              size="small"
              icon={<LinkOutlined />}
              onClick={() => navigate({ to: '/admin/webhooks' })}
            />
          </Tooltip>
          <Popconfirm title={t('common.confirmDelete')} onConfirm={() => deleteMutation.mutateAsync(r.id)}>
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <AdminContentCard noPadding>
      <Flex
        justify="space-between"
        align="center"
        gap={12}
        wrap
        className="admin-table-toolbar"
        style={{ padding: '14px 16px' }}
      >
        <Flex vertical gap={2} style={{ minWidth: 0 }}>
          <Text strong>{t('workflow.tabs.webhooks', { defaultValue: 'Webhooks' })}</Text>
          <Text type="secondary" style={{ fontSize: 12 }}>{t('workflow.webhookHint')}</Text>
        </Flex>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
          {t('workflow.addWebhook')}
        </Button>
      </Flex>

      <Table<WebhookSubscriptionDto>
        rowKey="id"
        columns={columns}
        dataSource={webhookSubs}
        size="small"
        pagination={false}
        loading={isLoading}
        locale={{
          emptyText: (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={t('workflow.noWebhooks', { defaultValue: 'Chưa có webhook nào' })}
            />
          ),
        }}
      />

      <Modal
        title={t('workflow.addWebhook')}
        open={modalOpen}
        onOk={handleCreate}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        confirmLoading={createMutation.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="endpointUrl"
            label={t('workflow.webhookUrl')}
            rules={[
              { required: true },
              { type: 'url', message: t('workflow.webhookUrlInvalid') },
            ]}
          >
            <Input placeholder="https://example.com/webhook" />
          </Form.Item>
          <Form.Item name="secret" label={t('workflow.webhookSecret')}>
            <Input.Password placeholder={t('workflow.webhookSecretHint')} />
          </Form.Item>
        </Form>
      </Modal>
    </AdminContentCard>
  );
}
