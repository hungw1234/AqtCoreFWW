import { useState } from 'react';
import { Table, Tag, Typography, Descriptions, Space, Button, Badge, Popconfirm } from 'antd';
import { appMessage } from '@/core/api/notification-bridge';
import {
  SafetyCertificateOutlined,
  TeamOutlined,
  UserOutlined,
  CrownOutlined,
  EyeOutlined,
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  KeyOutlined,
  CheckCircleOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useRoles, useDeleteRole, useActivateRole } from './roles-api';
import type { RoleDefinitionDto } from './roles-api';
import { RoleFormModal } from './role-form-modal';
import { RolePermissionsDrawer } from './role-permissions-drawer';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

const ROLE_ICONS: Record<string, React.ReactNode> = {
  SystemAdmin: <CrownOutlined />,
  Admin: <SafetyCertificateOutlined />,
  GovOfficer: <TeamOutlined />,
  Citizen: <UserOutlined />,
  Viewer: <EyeOutlined />,
};

const ROLE_COLORS: Record<string, string> = {
  SystemAdmin: 'red',
  Admin: 'volcano',
  GovOfficer: 'blue',
  Citizen: 'green',
  Viewer: 'default',
};

export function RolesPage() {
  const { t } = useTranslation();
  const { data: roles = [], isLoading } = useRoles();
  const deleteMutation = useDeleteRole();
  const activateMutation = useActivateRole();

  // Modal state
  const [modalOpen, setModalOpen] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleDefinitionDto | null>(null);

  // Permission drawer state
  const [permDrawerRole, setPermDrawerRole] = useState<RoleDefinitionDto | null>(null);

  function handleCreate() {
    setEditingRole(null);
    setModalOpen(true);
  }

  function handleEdit(role: RoleDefinitionDto) {
    setEditingRole(role);
    setModalOpen(true);
  }

  function handleCloseModal() {
    setModalOpen(false);
    setEditingRole(null);
  }

  async function handleDelete(id: string) {
    try {
      await deleteMutation.mutateAsync(id);
      appMessage.success(t('roles.disabledSuccess'));
    } catch {
      appMessage.error(t('roles.disableFailed'));
    }
  }

  async function handleActivate(id: string) {
    try {
      await activateMutation.mutateAsync(id);
      appMessage.success(t('roles.activatedSuccess'));
    } catch {
      appMessage.error(t('roles.activateFailed'));
    }
  }

  const isSystem = (r: RoleDefinitionDto) => r.roleType === 1;

  const columns: ColumnsType<RoleDefinitionDto> = [
    {
      title: '',
      key: 'icon',
      width: 48,
      render: (_, r) => (
        <span style={{ fontSize: 20 }}>{ROLE_ICONS[r.name] ?? <SafetyCertificateOutlined />}</span>
      ),
    },
    {
      title: t('roles.col.name'),
      key: 'name',
      render: (_, r) => (
        <Space direction="vertical" size={0}>
          <Tag color={ROLE_COLORS[r.name] ?? 'default'}>{r.name}</Tag>
          <Text type="secondary" style={{ fontSize: 12 }}>{r.description ?? ''}</Text>
        </Space>
      ),
    },
    {
      title: t('roles.col.code'),
      dataIndex: 'code',
      key: 'code',
      render: (code: string) => <Tag>{code}</Tag>,
    },
    {
      title: t('roles.col.type'),
      key: 'roleType',
      width: 100,
      render: (_, r) => (
        <Tag color={isSystem(r) ? 'red' : 'blue'}>
          {isSystem(r) ? t('roles.typeSystem') : t('roles.typeBusiness')}
        </Tag>
      ),
    },
    {
      title: t('roles.col.status'),
      key: 'isActive',
      width: 110,
      render: (_, r) => (
        <Badge
          status={r.isActive ? 'success' : 'default'}
          text={r.isActive ? t('roles.active') : t('roles.inactive')}
        />
      ),
    },
    {
      title: t('roles.col.actions'),
      key: 'actions',
      width: 180,
      render: (_, r) => (
        <Space size="small">
          <Button
            type="link"
            size="small"
            icon={<KeyOutlined />}
            onClick={() => setPermDrawerRole(r)}
            title={t('roles.permissions')}
          />
          <Button
            type="link"
            size="small"
            icon={<EditOutlined />}
            onClick={() => handleEdit(r)}
            title={t('common.edit')}
          />
          {!isSystem(r) && r.isActive && (
            <Popconfirm
              title={t('roles.disableConfirm')}
              description={t('roles.disableDesc')}
              onConfirm={() => handleDelete(r.id)}
              okText={t('roles.disable')}
              cancelText={t('common.cancel')}
            >
              <Button type="link" size="small" danger icon={<DeleteOutlined />} title={t('roles.disable')} />
            </Popconfirm>
          )}
          {!isSystem(r) && !r.isActive && (
            <Popconfirm
              title={t('roles.activateConfirm')}
              onConfirm={() => handleActivate(r.id)}
              okText={t('roles.activate')}
              cancelText={t('common.cancel')}
            >
              <Button
                type="link"
                size="small"
                icon={<CheckCircleOutlined />}
                title={t('roles.activate')}
                style={{ color: '#52c41a' }}
              />
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <>
      <AdminPageHeader
        title={t('roles.title')}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={handleCreate}>
            {t('roles.create')}
          </Button>
        }
      />

      <AdminContentCard>
        <Descriptions title={t('roles.authModel')} column={1} size="small">
          <Descriptions.Item label="RBAC">{t('roles.rbacDesc')}</Descriptions.Item>
          <Descriptions.Item label="ABAC">{t('roles.abacDesc')}</Descriptions.Item>
        </Descriptions>
      </AdminContentCard>

      <div style={{ marginTop: 'var(--page-gap, 24px)' }}>
        <AdminContentCard noPadding>
          <Table
            dataSource={roles}
            columns={columns}
            rowKey="id"
            pagination={false}
            bordered
            size="middle"
            loading={isLoading}
          />
        </AdminContentCard>
      </div>

      <RoleFormModal open={modalOpen} editRole={editingRole} onClose={handleCloseModal} />

      <RolePermissionsDrawer
        open={Boolean(permDrawerRole)}
        role={permDrawerRole}
        onClose={() => setPermDrawerRole(null)}
      />
    </>
  );
}
