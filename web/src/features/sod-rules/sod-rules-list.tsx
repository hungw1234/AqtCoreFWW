import { useState } from 'react';
import { Table, Button, Tag, Modal, Form, Select, Input, Switch } from 'antd';
import { PlusOutlined, EditOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useSodRules, useCreateSodRule, useUpdateSodRule, SOD_SEVERITY_OPTIONS, type SodRuleDto, type CreateSodRuleDto, type UpdateSodRuleDto } from './sod-rules-api';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { appMessage } from '@/core/api/notification-bridge';

const SEVERITY_COLORS: Record<string, string> = { Low: 'blue', Medium: 'orange', High: 'red' };

/** SoD Rules list content — extracted for tab composition */
export function SodRulesList() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [editingRule, setEditingRule] = useState<SodRuleDto | null>(null);
  const [form] = Form.useForm<CreateSodRuleDto | UpdateSodRuleDto>();

  const { data, isLoading } = useSodRules();
  const createRule = useCreateSodRule();
  const updateRule = useUpdateSodRule();

  const rules = data ?? [];

  const columns: ColumnsType<SodRuleDto> = [
    {
      title: 'Quyen A',
      dataIndex: 'permissionCodeA',
      key: 'permissionCodeA',
      width: 170,
      render: (v: string) => <Tag>{v}</Tag>,
    },
    {
      title: 'Quyen B',
      dataIndex: 'permissionCodeB',
      key: 'permissionCodeB',
      width: 170,
      render: (v: string) => <Tag>{v}</Tag>,
    },
    {
      title: t('sodRules.severity', 'Muc do'),
      dataIndex: 'severity',
      key: 'severity',
      width: 120,
      render: (v: string) => (
        <Tag color={SEVERITY_COLORS[v] ?? 'default'}>
          {t(`sodSeverity.${v.toLowerCase()}`, v)}
        </Tag>
      ),
    },
    {
      title: 'Mo ta',
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
      render: (v?: string) => v ?? '-',
    },
    {
      title: 'Kich hoat',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 90,
      render: (v: boolean) =>
        v ? <Tag color="green">Bat</Tag> : <Tag color="default">Tat</Tag>,
    },
    {
      title: '',
      key: 'actions',
      width: 50,
      render: (_, record) => (
        <Button
          size="small"
          icon={<EditOutlined />}
          onClick={() => {
            setEditingRule(record);
            form.setFieldsValue(record);
            setModalOpen(true);
          }}
        />
      ),
    },
  ];

  const handleSave = async () => {
    const values = await form.validateFields();
    try {
      if (editingRule) {
        await updateRule.mutateAsync({ ...(values as UpdateSodRuleDto), id: editingRule.id });
      } else {
        await createRule.mutateAsync(values as CreateSodRuleDto);
      }
      appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tac thanh cong' }));
      form.resetFields();
      setEditingRule(null);
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tac that bai' }));
    }
  };

  return (
    <>
      <div style={{ marginBottom: 16, display: 'flex', justifyContent: 'flex-end' }}>
        <Button
          type="primary"
          icon={<PlusOutlined />}
          onClick={() => {
            setEditingRule(null);
            form.resetFields();
            setModalOpen(true);
          }}
        >
          {t('common.add', 'Them')}
        </Button>
      </div>
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tim kiem...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        <Table<SodRuleDto>
          rowKey="id"
          columns={columns}
          dataSource={rules.filter((item) =>
            !searchText ||
            Object.values(item).some((v) =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase()),
            ),
          )}
          loading={isLoading}
          size="small"
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t) => `Tong ${t}` }}
        />
      </AdminContentCard>

      <Modal
        title={editingRule ? 'Sua quy tac SoD' : 'Tao quy tac SoD'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => {
          setModalOpen(false);
          setEditingRule(null);
          form.resetFields();
        }}
        confirmLoading={createRule.isPending || updateRule.isPending}
        okText={t('common.save', 'Luu')}
        cancelText={t('common.cancel', 'Huy')}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="permissionCodeA"
            label="Ma quyen A"
            rules={[{ required: true, message: 'Vui long nhap ma quyen A' }]}
          >
            <Input placeholder="VD: cases.approve" />
          </Form.Item>
          <Form.Item
            name="permissionCodeB"
            label="Ma quyen B"
            rules={[{ required: true, message: 'Vui long nhap ma quyen B' }]}
          >
            <Input placeholder="VD: cases.create" />
          </Form.Item>
          <Form.Item
            name="severity"
            label={t('sodRules.severity', 'Muc do')}
            rules={[{ required: true, message: t('sodRules.severityRequired', 'Vui long chon muc do') }]}
            initialValue="Low"
          >
            <Select options={SOD_SEVERITY_OPTIONS.map((opt) => ({ value: opt.value, label: t(opt.label, opt.value) }))} />
          </Form.Item>
          <Form.Item name="description" label="Mo ta">
            <Input.TextArea rows={3} placeholder="Giai thich ly do xung dot..." />
          </Form.Item>
          {editingRule && (
            <Form.Item name="isActive" label="Kich hoat" valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
        </Form>
      </Modal>
    </>
  );
}
