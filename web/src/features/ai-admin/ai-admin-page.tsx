import { useState } from 'react';
import { Tabs, Table, Button, Space, Typography, Popconfirm, Tag, Modal, Form, Input, Switch, InputNumber, Progress } from 'antd';
import { PlusOutlined, DeleteOutlined, EditOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useAiModelProfilesAdmin,
  useCreateAiModelProfile,
  useUpdateAiModelProfile,
  useDeleteAiModelProfile,
  useAiPromptTemplates,
  useCreateAiPromptTemplate,
  useUpdateAiPromptTemplate,
  useDeleteAiPromptTemplate,
  type AiModelProfileDto,
  type CreateAiModelProfileDto,
  type AiPromptTemplateDto,
  type CreateAiPromptTemplateDto,
} from './ai-admin-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;
const { TextArea } = Input;

// ─── Model Profiles tab ───────────────────────────────────────────────────────

function ModelProfilesTab() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<AiModelProfileDto | null>(null);
  const [form] = Form.useForm<CreateAiModelProfileDto>();

  const { data, isLoading } = useAiModelProfilesAdmin();
  const createMutation = useCreateAiModelProfile();
  const updateMutation = useUpdateAiModelProfile();
  const deleteMutation = useDeleteAiModelProfile();

  async function handleSave() {
    const values = await form.validateFields();
    try {
      if (editing) {
        await updateMutation.mutateAsync({ id: editing.id, body: values });
      } else {
        await createMutation.mutateAsync(values);
      }
      appMessage.success(t('common.success'));
      form.resetFields();
      setEditing(null);
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  const columns: ColumnsType<AiModelProfileDto> = [
    { title: t('aiAdmin.col.name'), dataIndex: 'name', key: 'name', ellipsis: true },
    { title: t('aiAdmin.col.provider'), dataIndex: 'provider', key: 'provider', width: 120 },
    { title: t('aiAdmin.col.modelId'), dataIndex: 'modelId', key: 'modelId', ellipsis: true, width: 180 },
    {
      title: t('aiAdmin.col.isDefault'),
      dataIndex: 'isDefault',
      key: 'isDefault',
      width: 90,
      render: (v: boolean) => v ? <Tag color="green">{t('common.yes')}</Tag> : <Tag>{t('common.no')}</Tag>,
    },
    {
      title: t('aiAdmin.col.tokenBudget'),
      key: 'tokenBudget',
      width: 200,
      render: (_, r) => {
        if (!r.tokenBudget) return '—';
        const used = r.tokensUsed ?? 0;
        const pct = Math.min(Math.round((used / r.tokenBudget) * 100), 100);
        return (
          <Space direction="vertical" size={0} style={{ width: '100%' }}>
            <Text style={{ fontSize: 12 }}>{used.toLocaleString()} / {r.tokenBudget.toLocaleString()}</Text>
            <Progress percent={pct} size="small" showInfo={false} strokeColor={pct > 90 ? '#ff4d4f' : '#1677ff'} />
          </Space>
        );
      },
    },
    {
      title: '',
      key: 'actions',
      width: 90,
      render: (_, record) => (
        <Space size="small">
          <Button size="small" icon={<EditOutlined />}
            onClick={() => { setEditing(record); form.setFieldsValue(record); setModalOpen(true); }} />
          <Popconfirm
            title={t('aiAdmin.deleteModelConfirm')}
            onConfirm={() => deleteMutation.mutate(record.id)}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
          >
            <Button size="small" icon={<DeleteOutlined />} danger loading={deleteMutation.isPending} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <div style={{ padding: '12px 24px', display: 'flex', justifyContent: 'flex-end' }}>
        <Button type="primary" icon={<PlusOutlined />}
          onClick={() => { setEditing(null); form.resetFields(); setModalOpen(true); }}>
          {t('common.add')}
        </Button>
      </div>
      <Table<AiModelProfileDto>
        rowKey="id"
        columns={columns}
        dataSource={data ?? []}
        loading={isLoading}
        size="small"
        scroll={{ x: 800 }}

        pagination={{ pageSize: 20 }}
      />
      <Modal
        title={editing ? t('aiAdmin.editModelTitle') : t('aiAdmin.createModelTitle')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => { setModalOpen(false); setEditing(null); form.resetFields(); }}
        confirmLoading={createMutation.isPending || updateMutation.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="name" label={t('aiAdmin.col.name')} rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="provider" label={t('aiAdmin.col.provider')} rules={[{ required: true }]}>
            <Input placeholder="OpenAI / AzureOpenAI / Anthropic" />
          </Form.Item>
          <Form.Item name="modelId" label={t('aiAdmin.col.modelId')} rules={[{ required: true }]}>
            <Input placeholder="gpt-4o / claude-3-5-sonnet-20241022" />
          </Form.Item>
          <Form.Item name="tokenBudget" label={t('aiAdmin.col.tokenBudget')}>
            <InputNumber style={{ width: '100%' }} min={0} />
          </Form.Item>
          <Form.Item name="isDefault" label={t('aiAdmin.col.isDefault')} valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}

// ─── Prompt Templates tab ─────────────────────────────────────────────────────

function PromptTemplatesTab() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<AiPromptTemplateDto | null>(null);
  const [form] = Form.useForm<CreateAiPromptTemplateDto>();

  const { data, isLoading } = useAiPromptTemplates();
  const createMutation = useCreateAiPromptTemplate();
  const updateMutation = useUpdateAiPromptTemplate();
  const deleteMutation = useDeleteAiPromptTemplate();

  async function handleSave() {
    const values = await form.validateFields();
    try {
      if (editing) {
        await updateMutation.mutateAsync({ id: editing.id, body: values });
      } else {
        await createMutation.mutateAsync(values);
      }
      appMessage.success(t('common.success'));
      form.resetFields();
      setEditing(null);
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  const columns: ColumnsType<AiPromptTemplateDto> = [
    { title: t('aiAdmin.col.promptName'), dataIndex: 'name', key: 'name', ellipsis: true },
    { title: t('aiAdmin.col.description'), dataIndex: 'description', key: 'description', ellipsis: true, render: (v?: string) => v ?? '—' },
    {
      title: t('aiAdmin.col.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 130,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY'),
    },
    {
      title: '',
      key: 'actions',
      width: 90,
      render: (_, record) => (
        <Space size="small">
          <Button size="small" icon={<EditOutlined />}
            onClick={() => { setEditing(record); form.setFieldsValue(record); setModalOpen(true); }} />
          <Popconfirm
            title={t('aiAdmin.deletePromptConfirm')}
            onConfirm={() => deleteMutation.mutate(record.id)}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
          >
            <Button size="small" icon={<DeleteOutlined />} danger loading={deleteMutation.isPending} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <div style={{ padding: '12px 24px', display: 'flex', justifyContent: 'flex-end' }}>
        <Button type="primary" icon={<PlusOutlined />}
          onClick={() => { setEditing(null); form.resetFields(); setModalOpen(true); }}>
          {t('common.add')}
        </Button>
      </div>
      <Table<AiPromptTemplateDto>
        rowKey="id"
        columns={columns}
        dataSource={data ?? []}
        loading={isLoading}
        size="small"

        pagination={{ pageSize: 20 }}
      />
      <Modal
        title={editing ? t('aiAdmin.editPromptTitle') : t('aiAdmin.createPromptTitle')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => { setModalOpen(false); setEditing(null); form.resetFields(); }}
        confirmLoading={createMutation.isPending || updateMutation.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        width={640}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="name" label={t('aiAdmin.col.promptName')} rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label={t('aiAdmin.col.description')}>
            <Input />
          </Form.Item>
          <Form.Item name="templateText" label={t('aiAdmin.col.templateText')} rules={[{ required: true }]}>
            <TextArea rows={8} style={{ fontFamily: 'monospace' }} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}

// ─── Main AI Admin page ───────────────────────────────────────────────────────

// AiAdminPage — tabs for AiModelProfiles (with token budget) and AiPromptTemplates
export function AiAdminPage() {
  const { t } = useTranslation();

  return (
    <div>
      <AdminPageHeader title={t('aiAdmin.title')} />
      <AdminContentCard noPadding>
        <Tabs
          defaultActiveKey="models"
          style={{ padding: '0 24px' }}
          items={[
            {
              key: 'models',
              label: t('aiAdmin.tab.models'),
              children: <ModelProfilesTab />,
            },
            {
              key: 'prompts',
              label: t('aiAdmin.tab.prompts'),
              children: <PromptTemplatesTab />,
            },
          ]}
        />
      </AdminContentCard>
    </div>
  );
}
