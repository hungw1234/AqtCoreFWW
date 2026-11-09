import { useState } from 'react';
import { Table, Button, Space, Tag, Switch, Modal, Form, Input, InputNumber, Select, Tooltip } from 'antd';
import { PlusOutlined, EditOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useMenus,
  useCreateMenu,
  useUpdateMenu,
  type MenuDto,
  type CreateMenuRequest,
  type UpdateMenuRequest,
} from './menu-api';
import { usePermissions } from '@/features/auth/use-permissions';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';

// ─── Menu Form Modal ──────────────────────────────────────────────────────────

interface MenuFormModalProps {
  open: boolean;
  editing: MenuDto | null;
  parentOptions: { value: string; label: string }[];
  onClose: () => void;
}

function MenuFormModal({ open, editing, parentOptions, onClose }: MenuFormModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<CreateMenuRequest & { isActive?: boolean }>();
  const createMutation = useCreateMenu();
  const updateMutation = useUpdateMenu();

  async function handleSave() {
    const values = await form.validateFields();
    try {
      if (editing) {
        const body: UpdateMenuRequest = {
          parentId: values.parentId ?? null,
          title: values.title,
          icon: values.icon ?? null,
          route: values.route ?? null,
          sortOrder: values.sortOrder ?? 0,
          isActive: (values as { isActive?: boolean }).isActive ?? true,
          permissionCodes: values.permissionCodes ?? [],
        };
        await updateMutation.mutateAsync({ id: editing.id, body });
      } else {
        await createMutation.mutateAsync({
          parentId: values.parentId ?? null,
          code: values.code,
          title: values.title,
          icon: values.icon ?? null,
          route: values.route ?? null,
          sortOrder: values.sortOrder ?? 0,
          permissionCodes: values.permissionCodes ?? [],
        });
      }
      appMessage.success(t('common.success'));
      form.resetFields();
      onClose();
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  function handleOpen() {
    if (editing) {
      form.setFieldsValue({ ...editing, parentId: editing.parentId ?? undefined, permissionCodes: editing.permissionCodes });
    } else {
      form.resetFields();
    }
  }

  return (
    <Modal
      title={editing ? t('menus.editTitle') : t('menus.createTitle')}
      open={open}
      onOk={handleSave}
      onCancel={() => { form.resetFields(); onClose(); }}
      confirmLoading={createMutation.isPending || updateMutation.isPending}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      afterOpenChange={(visible) => { if (visible) handleOpen(); }}
      destroyOnHidden
      width={520}
    >
      <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
        {!editing && (
          <Form.Item name="code" label={t('menus.col.code')} rules={[{ required: true }]}>
            <Input placeholder="e.g. admin.users" />
          </Form.Item>
        )}
        <Form.Item name="title" label={t('menus.col.title')} rules={[{ required: true }]}>
          <Input />
        </Form.Item>
        <Form.Item name="parentId" label={t('menus.col.parent')}>
          <Select allowClear placeholder={t('menus.noParent')} options={parentOptions} />
        </Form.Item>
        <Form.Item name="icon" label={t('menus.col.icon')}>
          <Input placeholder="e.g. SettingOutlined" />
        </Form.Item>
        <Form.Item name="route" label={t('menus.col.route')}>
          <Input placeholder="/admin/..." />
        </Form.Item>
        <Form.Item name="sortOrder" label={t('menus.col.sortOrder')} initialValue={0}>
          <InputNumber style={{ width: '100%' }} min={0} />
        </Form.Item>
        <Form.Item name="permissionCodes" label={t('menus.col.permissions')}>
          <Select mode="tags" placeholder="e.g. admin.read" tokenSeparators={[',']} />
        </Form.Item>
        {editing && (
          <Form.Item name="isActive" label={t('menus.col.active')} valuePropName="checked" initialValue>
            <Switch />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
}

// ─── Menu Admin Page ──────────────────────────────────────────────────────────

// MenuAdminPage — CRUD table for menu items (admin only)
export function MenuAdminPage() {
  const { t } = useTranslation();
  const { hasPermission } = usePermissions();
  // Global rows (TenantId=null) are platform-tier — SYSTEM.CONFIG.MANAGE is the
  // SystemAdmin-only proxy permission per RolePermissions seeder excluded list.
  const isSystemAdmin = hasPermission('SYSTEM.CONFIG.MANAGE');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<MenuDto | null>(null);
  const [searchText, setSearchText] = useState('');

  const { data = [], isLoading } = useMenus();
  const total = data.length;

  const parentOptions = data.map((m) => ({ value: m.id, label: `${m.code} — ${m.title}` }));

  const columns: ColumnsType<MenuDto> = [
    { title: t('menus.col.code'), dataIndex: 'code', key: 'code', width: 180, ellipsis: true },
    {
      title: t('menus.col.title'),
      dataIndex: 'title',
      key: 'title',
      ellipsis: true,
      render: (title: string, record) =>
        record.tenantId == null
          ? <Space size={6}><span>{title}</span><Tag color="geekblue" style={{ fontSize: 10 }}>{t('menus.systemTag', { defaultValue: 'Hệ thống' })}</Tag></Space>
          : title,
    },
    {
      title: t('menus.col.route'),
      dataIndex: 'route',
      key: 'route',
      ellipsis: true,
      render: (v?: string) => v ?? '—',
    },
    {
      title: t('menus.col.sortOrder'),
      dataIndex: 'sortOrder',
      key: 'sortOrder',
      width: 100,
      sorter: (a, b) => a.sortOrder - b.sortOrder,
    },
    {
      title: t('menus.col.permissions'),
      dataIndex: 'permissionCodes',
      key: 'permissionCodes',
      render: (codes: string[]) =>
        codes.length > 0
          ? <Space wrap>{codes.map((c) => <Tag key={c} color="blue">{c}</Tag>)}</Space>
          : <span style={{ color: '#aaa' }}>—</span>,
    },
    {
      title: t('menus.col.active'),
      dataIndex: 'isActive',
      key: 'isActive',
      width: 80,
      render: (v: boolean) => <Tag color={v ? 'green' : 'default'}>{v ? t('common.yes') : t('common.no')}</Tag>,
    },
    {
      title: '',
      key: 'actions',
      width: 60,
      render: (_, record) => {
        const isGlobal = record.tenantId == null;
        const disabled = isGlobal && !isSystemAdmin;
        const btn = (
          <Button
            size="small"
            icon={<EditOutlined />}
            disabled={disabled}
            onClick={() => { setEditing(record); setModalOpen(true); }}
          />
        );
        return disabled
          ? <Tooltip title={t('menus.systemReadOnly', { defaultValue: 'Menu hệ thống — chỉ SystemAdmin mới sửa được' })}>{btn}</Tooltip>
          : btn;
      },
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('menus.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => { setEditing(null); setModalOpen(true); }}
          >
            {t('common.add')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        <Table<MenuDto>
          rowKey="id"
          columns={columns}
          dataSource={data.filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          pagination={{ pageSize: 50, hideOnSinglePage: true, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
          scroll={{ x: 900 }}
        />
      </AdminContentCard>

      <MenuFormModal
        open={modalOpen}
        editing={editing}
        parentOptions={parentOptions}
        onClose={() => { setModalOpen(false); setEditing(null); }}
      />
    </div>
  );
}
