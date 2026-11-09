// General tab — read overview + inline edit for definition metadata (name, description, status info)

import { useState } from 'react';
import { Button, Form, Input, Flex, Typography, Tag, Space, Divider } from 'antd';
import {
  EditOutlined,
  ClockCircleOutlined,
  ApartmentOutlined,
  BranchesOutlined,
  TagOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useUpdateWorkflowDefinition } from '../workflow-api';
import type { WorkflowDefinitionDto } from '../workflow-types';
import dayjs from 'dayjs';
import { appMessage } from '@/core/api/notification-bridge';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Title, Text, Paragraph } = Typography;

interface Props {
  definition: WorkflowDefinitionDto;
}

// StatTile — compact metric tile (icon + label + value) using design tokens so dark mode stays correct.
function StatTile({ icon, label, value }: { icon: React.ReactNode; label: string; value: React.ReactNode }) {
  return (
    <Flex
      vertical
      gap={4}
      style={{
        flex: 1,
        minWidth: 120,
        padding: '14px 16px',
        background: 'var(--gov-bg-muted)',
        border: '1px solid var(--gov-border-color)',
        borderRadius: 'var(--radius-md)',
      }}
    >
      <Flex align="center" gap={6} style={{ color: 'var(--gov-text-secondary)', fontSize: 12 }}>
        <span style={{ color: 'var(--gov-navy)', display: 'flex' }}>{icon}</span>
        {label}
      </Flex>
      <Text strong style={{ fontSize: 20, lineHeight: 1.2 }}>{value}</Text>
    </Flex>
  );
}

export function DefinitionGeneralTab({ definition }: Props) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [form] = Form.useForm();
  const updateMutation = useUpdateWorkflowDefinition(definition.id);

  const handleSave = async () => {
    const values = await form.validateFields();
    await updateMutation.mutateAsync(values);
    appMessage.success(t('common.saved', { defaultValue: 'Đã lưu' }));
    setEditing(false);
  };

  if (editing) {
    return (
      <AdminContentCard>
        <Title level={5} style={{ marginTop: 0, marginBottom: 16 }}>
          {t('workflow.tabs.general', { defaultValue: 'Tổng quan' })}
        </Title>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ name: definition.name, description: definition.description }}
          style={{ maxWidth: 600 }}
        >
          <Form.Item name="name" label={t('workflow.col.name')} rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label={t('workflow.description')}>
            <Input.TextArea rows={4} />
          </Form.Item>
          <Space>
            <Button type="primary" onClick={handleSave} loading={updateMutation.isPending}>
              {t('common.save')}
            </Button>
            <Button onClick={() => setEditing(false)}>{t('common.cancel')}</Button>
          </Space>
        </Form>
      </AdminContentCard>
    );
  }

  return (
    <AdminContentCard>
      {/* Header: section title + edit action top-right */}
      <Flex justify="space-between" align="center" gap={12} wrap style={{ marginBottom: 20 }}>
        <Flex vertical gap={2} style={{ minWidth: 0 }}>
          <Title level={5} style={{ margin: 0 }}>{definition.name}</Title>
          <Space size={6} wrap>
            <Text type="secondary" style={{ fontSize: 12 }}>{t('workflow.definitionKey')}:</Text>
            <Text code style={{ fontSize: 12 }}>{definition.definitionKey}</Text>
          </Space>
        </Flex>
        <Button icon={<EditOutlined />} onClick={() => setEditing(true)}>
          {t('common.edit')}
        </Button>
      </Flex>

      {/* Stat tiles — key numeric facts */}
      <Flex gap={12} wrap style={{ marginBottom: 20 }}>
        <StatTile icon={<ApartmentOutlined />} label={t('workflow.col.states')} value={definition.states.length} />
        <StatTile icon={<BranchesOutlined />} label={t('workflow.col.transitions')} value={definition.transitions.length} />
        <StatTile icon={<TagOutlined />} label={t('workflow.version')} value={`v${definition.version}`} />
      </Flex>

      {/* Status row */}
      <Flex align="center" gap={8} style={{ marginBottom: 16 }}>
        <Text type="secondary" style={{ fontSize: 13 }}>{t('workflow.isLatest')}:</Text>
        <Tag color={definition.isLatest ? 'green' : 'default'}>
          {definition.isLatest ? t('common.yes') : t('common.no')}
        </Tag>
        <Divider type="vertical" style={{ margin: '0 4px' }} />
        <ClockCircleOutlined style={{ color: 'var(--gov-text-muted)' }} />
        <Text type="secondary" style={{ fontSize: 13 }}>
          {t('workflow.col.createdAt')}: {dayjs(definition.createdAt).format('DD/MM/YYYY HH:mm')}
        </Text>
      </Flex>

      <Divider style={{ margin: '0 0 12px' }} />

      {/* Description block */}
      <div>
        <Text type="secondary" style={{ fontSize: 13, display: 'block', marginBottom: 4 }}>
          {t('workflow.description')}
        </Text>
        <Paragraph style={{ margin: 0, color: definition.description ? undefined : 'var(--gov-text-muted)' }}>
          {definition.description || '—'}
        </Paragraph>
      </div>
    </AdminContentCard>
  );
}
