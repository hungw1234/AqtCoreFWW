// view-manager.tsx — CRUD UI for saved views with create/edit modal
import { useState } from 'react';
import {
  Card,
  Table,
  Button,
  Space,
  Tag,
  Modal,
  Form,
  Input,
  Select,
  InputNumber,
  Checkbox,
  Divider,
  Row,
  Col,
  Popconfirm,
  Tooltip,
} from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { ColumnsType } from 'antd/es/table';
import type { FormFieldDto } from '../form-types';
import type { ViewDefinitionDto, ViewType } from './view-types';
import { useViews, useCreateView, useUpdateView, useDeleteView } from './view-api';
import { ViewColumnEditor } from './view-column-editor';
import { appMessage } from '@/core/api/notification-bridge';

const VIEW_TYPE_OPTIONS: { value: ViewType; label: string }[] = [
  { value: 'List', label: 'List' },
  { value: 'Grid', label: 'Grid' },
  { value: 'Kanban', label: 'Kanban' },
  { value: 'Calendar', label: 'Calendar' },
];

interface ViewManagerProps {
  templateId: string;
  fields: FormFieldDto[];
}

export function ViewManager({ templateId, fields }: ViewManagerProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm();
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<ViewDefinitionDto | null>(null);

  // Scope views per template — entityType includes templateId for isolation
  const entityType = `FormSubmission:${templateId}`;
  const { data: viewsData, isLoading } = useViews(entityType);
  const createView = useCreateView();
  const updateView = useUpdateView();
  const deleteView = useDeleteView();

  function openCreate() {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ type: 'List', defaultSortDir: 'asc', pageSize: 20 });
    setModalOpen(true);
  }

  function openEdit(record: ViewDefinitionDto) {
    setEditing(record);
    form.setFieldsValue({
      name: record.name,
      type: record.type,
      defaultSortField: record.defaultSortField,
      defaultSortDir: record.defaultSortDir || 'asc',
      pageSize: record.pageSize || 20,
      isDefault: record.isDefault,
      columns: record.columns,
    });
    setModalOpen(true);
  }

  async function handleSave() {
    try {
      const values = await form.validateFields();
      if (editing) {
        const cols = (values.columns ?? editing.columns ?? []).map(
          (c: Record<string, unknown>, i: number) => ({ ...c, displayOrder: i + 1 })
        );
        updateView.mutate(
          { id: editing.id, name: values.name, defaultSortField: values.defaultSortField, defaultSortDir: values.defaultSortDir, pageSize: values.pageSize, isDefault: values.isDefault, columns: cols },
          { onSuccess: () => { setModalOpen(false); appMessage.success(t('page.forms.views.updateSuccess', { defaultValue: 'View updated' })); } }
        );
      } else {
        const cols = (values.columns ?? []).map((c: Record<string, unknown>, i: number) => ({
          ...c, displayOrder: i + 1,
        }));
        createView.mutate(
          { ...values, entityType, columns: cols },
          { onSuccess: () => { setModalOpen(false); appMessage.success(t('page.forms.views.createSuccess', { defaultValue: 'View created' })); } }
        );
      }
    } catch { /* validation errors */ }
  }

  const fieldOptions = fields
    .filter((f) => !['Section', 'Label', 'Divider'].includes(f.type))
    .map((f) => ({ value: f.fieldKey, label: f.labelVi || f.fieldKey }));

  const tableColumns: ColumnsType<ViewDefinitionDto> = [
    { title: t('page.forms.views.colName', { defaultValue: 'Name' }), dataIndex: 'name' },
    {
      title: t('page.forms.views.colType', { defaultValue: 'Type' }), dataIndex: 'type', width: 100,
      render: (v: string) => <Tag>{v}</Tag>,
    },
    {
      title: t('page.forms.views.colColumns', { defaultValue: 'Columns' }), key: 'colCount', width: 80,
      render: (_, r) => r.columns?.length ?? 0,
    },
    {
      title: t('page.forms.views.colDefault', { defaultValue: 'Default' }), dataIndex: 'isDefault', width: 80,
      render: (v: boolean) => v ? <Tag color="green">Default</Tag> : null,
    },
    {
      title: t('page.forms.views.colActions', { defaultValue: 'Actions' }), key: 'actions', width: 140,
      render: (_, record) => (
        <Space size={4}>
          <Tooltip title={t('common.edit', { defaultValue: 'Chỉnh sửa' })}>
            <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(record)} />
          </Tooltip>
          <Popconfirm title={t('page.forms.views.deleteConfirm', { defaultValue: 'Delete this view?' })}
            onConfirm={() => deleteView.mutate(record.id, {
              onSuccess: () => appMessage.success(t('page.forms.views.deleteSuccess', { defaultValue: 'View deleted' })),
            })}>
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title={t('page.forms.views.title', { defaultValue: 'Saved Views' })}
        extra={
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            {t('page.forms.views.createBtn', { defaultValue: 'Create View' })}
          </Button>
        }
        size="small"
      >
        <Table<ViewDefinitionDto>
          rowKey="id"
          dataSource={viewsData?.items ?? []}
          columns={tableColumns}
          loading={isLoading}
          size="small"
          pagination={false}
        />
      </Card>

      <Modal
        title={editing
          ? t('page.forms.views.editTitle', { defaultValue: 'Edit View' })
          : t('page.forms.views.createTitle', { defaultValue: 'Create View' })}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSave}
        confirmLoading={createView.isPending || updateView.isPending}
        width={800}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" size="small">
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="name" label={t('page.forms.views.name', { defaultValue: 'Name' })}
                rules={[{ required: true }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="type" label={t('page.forms.views.type', { defaultValue: 'Type' })}
                initialValue="List">
                <Select options={VIEW_TYPE_OPTIONS} disabled={!!editing} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="defaultSortField" label={t('page.forms.views.sortField', { defaultValue: 'Sort Field' })}>
                <Select allowClear options={fieldOptions} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="defaultSortDir" label={t('page.forms.views.sortDir', { defaultValue: 'Sort Direction' })}
                initialValue="asc">
                <Select options={[{ value: 'asc', label: 'Ascending' }, { value: 'desc', label: 'Descending' }]} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="pageSize" label={t('page.forms.views.pageSize', { defaultValue: 'Page Size' })}
                initialValue={20}>
                <InputNumber min={5} max={100} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="isDefault" valuePropName="checked">
            <Checkbox>{t('page.forms.views.setDefault', { defaultValue: 'Set as default view' })}</Checkbox>
          </Form.Item>

          <Divider>{t('page.forms.views.columnSection', { defaultValue: 'Columns' })}</Divider>
          <Form.Item name="columns">
            <ViewColumnEditor fields={fields} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
