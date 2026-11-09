// Delegation Rules management page — CRUD table for workflow task delegation rules.
// Requires WORKFLOW.DELEGATION.MANAGE permission (enforced by route + BE controller).

import { useState } from 'react';
import { Typography, Button, Space, Table, Tag, Popconfirm } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useDelegationRules, useDeleteDelegationRule } from './workflow-api';
import { DelegationRuleFormDialog } from './workflow-delegation-rule-form-dialog';
import type { DelegationRuleDto } from './workflow-types';
import { appMessage } from '@/core/api/notification-bridge';

const { Title } = Typography;

export function DelegationRulesPage() {
  const { t } = useTranslation();
  const { data: rules = [], isLoading } = useDelegationRules();
  const deleteMutation = useDeleteDelegationRule();
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editRule, setEditRule] = useState<DelegationRuleDto | null>(null);

  const handleAdd = () => {
    setEditRule(null);
    setDialogOpen(true);
  };

  const handleEdit = (rule: DelegationRuleDto) => {
    setEditRule(rule);
    setDialogOpen(true);
  };

  const handleDelete = async (id: string) => {
    await deleteMutation.mutateAsync(id);
    appMessage.success(t('common.deleted'));
  };

  const handleDialogClose = () => {
    setDialogOpen(false);
    setEditRule(null);
  };

  const columns = [
    {
      title: t('workflow.delegation.delegatorUserId'),
      dataIndex: 'delegatorUserId',
      key: 'delegatorUserId',
      ellipsis: true,
    },
    {
      title: t('workflow.delegation.delegateUserId'),
      dataIndex: 'delegateUserId',
      key: 'delegateUserId',
      ellipsis: true,
    },
    {
      title: t('workflow.delegation.validFrom'),
      dataIndex: 'validFrom',
      key: 'validFrom',
      render: (v: string) => new Date(v).toLocaleString(),
    },
    {
      title: t('workflow.delegation.validUntil'),
      dataIndex: 'validUntil',
      key: 'validUntil',
      render: (v: string) => new Date(v).toLocaleString(),
    },
    {
      title: t('workflow.delegation.isActive'),
      dataIndex: 'isActive',
      key: 'isActive',
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>
          {v ? t('workflow.active') : t('workflow.inactive')}
        </Tag>
      ),
    },
    {
      title: t('common.actions'),
      key: 'actions',
      width: 120,
      render: (_: unknown, record: DelegationRuleDto) => (
        <Space size="small">
          <Button
            icon={<EditOutlined />}
            size="small"
            onClick={() => handleEdit(record)}
          />
          <Popconfirm
            title={t('workflow.delegation.deleteConfirm')}
            onConfirm={() => handleDelete(record.id)}
            okText={t('common.yes')}
            cancelText={t('common.no')}
          >
            <Button
              icon={<DeleteOutlined />}
              size="small"
              danger
              loading={deleteMutation.isPending}
            />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Space style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between' }}>
        <Title level={4} style={{ margin: 0 }}>
          {t('workflow.delegation.pageTitle')}
        </Title>
        <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>
          {t('workflow.delegation.addRule')}
        </Button>
      </Space>

      <Table
        rowKey="id"
        dataSource={rules}
        columns={columns}
        loading={isLoading}
        size="small"
        pagination={{ pageSize: 20 }}
      />

      <DelegationRuleFormDialog
        open={dialogOpen}
        editRule={editRule}
        onClose={handleDialogClose}
      />
    </div>
  );
}
