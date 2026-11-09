import { useState } from 'react';
import {
  Table,
  Button,
  Space,
  Tag,
  Modal,
  Form,
  Input,
  Popconfirm,
  Flex,
} from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import {
  useGroups,
  useCreateGroup,
  useUpdateGroup,
  useDeleteGroup,
  type GroupDto,
  type CreateGroupDto,
} from './groups-api';
import { GroupDetailDrawer } from './group-detail-drawer';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';

// GroupsPage — list user groups; click row name to open detail drawer
export function GroupsPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [editingGroup, setEditingGroup] = useState<GroupDto | null>(null);
  const [searchText, setSearchText] = useState('');
  const [drawerGroupId, setDrawerGroupId] = useState<string | null>(null);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [form] = Form.useForm<CreateGroupDto>();

  const { data, isLoading } = useGroups();
  const createGroup = useCreateGroup();
  const updateGroup = useUpdateGroup();
  const deleteGroup = useDeleteGroup();

  const groups = data ?? [];
  const total = groups.length;

  // Bulk delete — allSettled so partial failures are reported, not silently swallowed
  async function handleBulkDelete() {
    const results = await Promise.allSettled(selectedIds.map(id => deleteGroup.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục xóa thất bại` }));
    } else {
      appMessage.success(t('common.bulkDeleteSuccess', { defaultValue: 'Xóa thành công' }));
    }
    setSelectedIds([]);
  }

  const columns: ColumnsType<GroupDto> = [
    {
      title: 'Mã nhóm',
      dataIndex: 'code',
      key: 'code',
      width: 140,
    },
    {
      title: 'Tên nhóm',
      dataIndex: 'name',
      key: 'name',
      ellipsis: true,
      render: (v, record) => (
        <Button type="link" style={{ padding: 0 }} onClick={() => setDrawerGroupId(record.id)}>
          {v}
        </Button>
      ),
    },
    {
      title: 'Mô tả',
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
      render: (v?: string) => v ?? '—',
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 100,
      render: (v: boolean) =>
        v ? <Tag color="green">Hoạt động</Tag> : <Tag color="default">Tắt</Tag>,
    },
    {
      title: 'Ngày tạo',
      dataIndex: 'createdAtUtc',
      key: 'createdAtUtc',
      width: 130,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY'),
    },
    {
      title: '',
      key: 'actions',
      width: 90,
      render: (_, record) => (
        <Space size="small">
          <Button
            size="small"
            icon={<EditOutlined />}
            onClick={(e) => {
              e.stopPropagation();
              setEditingGroup(record);
              form.setFieldsValue(record);
              setModalOpen(true);
            }}
          />
          <Popconfirm
            title="Xóa nhóm này?"
            onConfirm={() => deleteGroup.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.deleteSuccess', { defaultValue: 'Xóa thành công' })),
              onError: () => appMessage.error(t('common.deleteFailed', { defaultValue: 'Xóa thất bại' })),
            })}
            okText={t('common.confirm', 'Xác nhận')}
            cancelText={t('common.cancel', 'Hủy')}
          >
            <Button
              danger
              icon={<DeleteOutlined />}
              size="small"
              loading={deleteGroup.isPending}
              onClick={(e) => e.stopPropagation()}
            />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const handleSave = async () => {
    const values = await form.validateFields();
    try {
      if (editingGroup) {
        await updateGroup.mutateAsync({ ...values, id: editingGroup.id });
      } else {
        await createGroup.mutateAsync(values);
      }
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
      form.resetFields();
      setEditingGroup(null);
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }));
    }
  };

  return (
    <div>
      <AdminPageHeader
        title="Nhóm người dùng"
        stats={{ total, label: 'nhóm' }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => { setEditingGroup(null); form.resetFields(); setModalOpen(true); }}
          >
            {t('common.add', 'Thêm')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        {selectedIds.length > 0 && (
          <Flex gap={8} style={{ padding: '0 24px 8px' }}>
            <Popconfirm
              title={t('common.bulkDeleteConfirm', { defaultValue: `Xóa ${selectedIds.length} nhóm đã chọn?` })}
              onConfirm={handleBulkDelete}
              okText={t('common.yes', { defaultValue: 'Có' })}
              cancelText={t('common.no', { defaultValue: 'Không' })}
            >
              <Button danger size="small">
                {t('common.deleteSelected', { defaultValue: `Xóa (${selectedIds.length})` })}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<GroupDto>
          rowKey="id"
          columns={columns}
          dataSource={groups.filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
        />
      </AdminContentCard>

      {/* Create / Edit modal */}
      <Modal
        title={editingGroup ? 'Sửa nhóm' : 'Tạo nhóm mới'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => { setModalOpen(false); setEditingGroup(null); form.resetFields(); }}
        confirmLoading={createGroup.isPending || updateGroup.isPending}
        okText={t('common.save', 'Lưu')}
        cancelText={t('common.cancel', 'Hủy')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="code"
            label="Mã nhóm"
            rules={[{ required: true, message: 'Vui lòng nhập mã nhóm' }]}
          >
            <Input placeholder="VD: PHONG_HANH_CHINH" style={{ textTransform: 'uppercase' }} />
          </Form.Item>
          <Form.Item name="name" label="Tên nhóm" rules={[{ required: true, message: 'Vui lòng nhập tên nhóm' }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Mô tả">
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      {/* Detail drawer — members + role IDs tabs */}
      <GroupDetailDrawer
        groupId={drawerGroupId}
        onClose={() => setDrawerGroupId(null)}
      />
    </div>
  );
}
