import { useState } from 'react';
import { Table, Button, Space, Popconfirm, Tag, Modal, Form, Input, Select, Typography, Flex, Tooltip } from 'antd';
import { PlusOutlined, DeleteOutlined, EditOutlined, DownloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useTemplates,
  useTemplate,
  useCreateTemplate,
  useUpdateTemplate,
  useDeleteTemplate,
  useGeneratePreview,
  type DocumentTemplateDto,
  type CreateTemplateDto,
  type UpdateTemplateDto,
} from './templates-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { useDebouncedValue } from '@/core/hooks/use-debounced-value';

const { TextArea } = Input;
const { Option } = Select;

// ─── Preview modal ────────────────────────────────────────────────────────────

interface PreviewModalProps {
  templateId: string;
  open: boolean;
  onClose: () => void;
}

function PreviewModal({ templateId, open, onClose }: PreviewModalProps) {
  const { t } = useTranslation();
  const [dataJson, setDataJson] = useState('{\n  \n}');
  const generateMutation = useGeneratePreview();

  async function handleDownload() {
    try {
      JSON.parse(dataJson);
    } catch {
      appMessage.error(t('templates.invalidJson'));
      return;
    }
    try {
      const blob = await generateMutation.mutateAsync({ templateId, dataJson });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `preview-${templateId}.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      appMessage.error(t('templates.previewError'));
    }
  }

  return (
    <Modal
      title={t('templates.previewTitle')}
      open={open}
      onCancel={onClose}
      onOk={handleDownload}
      okText={<><DownloadOutlined /> {t('templates.downloadBtn')}</>}
      confirmLoading={generateMutation.isPending}
      okButtonProps={{ icon: <DownloadOutlined /> }}
      destroyOnHidden
    >
      <Typography.Paragraph type="secondary">{t('templates.previewHint')}</Typography.Paragraph>
      <TextArea
        rows={8}
        value={dataJson}
        onChange={(e) => setDataJson(e.target.value)}
        style={{ fontFamily: 'monospace' }}
      />
    </Modal>
  );
}

// ─── Edit modal ───────────────────────────────────────────────────────────────

interface EditModalProps {
  templateId: string | null;
  open: boolean;
  onClose: () => void;
}

function EditModal({ templateId, open, onClose }: EditModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<CreateTemplateDto>();
  const { data: detail, isLoading } = useTemplate(templateId ?? '');
  const createMutation = useCreateTemplate();
  const updateMutation = useUpdateTemplate();

  // Populate form when detail loads
  if (detail && !isLoading) {
    form.setFieldsValue(detail);
  }

  async function handleSave() {
    const values = await form.validateFields();
    try {
      if (templateId) {
        const updateBody: UpdateTemplateDto = {
          name: values.name,
          description: values.description,
          templateContent: values.templateContent,
        };
        await updateMutation.mutateAsync({ id: templateId, body: updateBody });
      } else {
        await createMutation.mutateAsync(values);
      }
      appMessage.success(t('common.success'));
      form.resetFields();
      onClose();
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  return (
    <Modal
      title={templateId ? t('templates.editTitle') : t('templates.createTitle')}
      open={open}
      onOk={handleSave}
      onCancel={() => { form.resetFields(); onClose(); }}
      confirmLoading={createMutation.isPending || updateMutation.isPending}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      width={720}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
        <Form.Item
          name="name"
          label={t('templates.col.name')}
          validateFirst
          rules={[
            { required: true },
            // Mirrors BE CreateDocumentTemplateCommandValidator.cs — keep in sync
            { max: 300, message: t('common.validation.maxLength', { max: 300 }) },
          ]}
        >
          <Input />
        </Form.Item>
        {!templateId && (
          <>
            <Form.Item
              name="code"
              label={t('templates.col.code', { defaultValue: 'Mã' })}
              validateFirst
              rules={[
                { required: true },
                // Mirrors BE CreateDocumentTemplateCommandValidator.cs — keep in sync
                { max: 100, message: t('common.validation.maxLength', { max: 100 }) },
              ]}
            >
              <Input />
            </Form.Item>
            <Form.Item name="outputFormat" label={t('templates.col.outputFormat', { defaultValue: 'Định dạng' })} rules={[{ required: true }]}>
              <Select>
                <Option value="Pdf">PDF</Option>
                <Option value="Docx">DOCX</Option>
                <Option value="Html">HTML</Option>
              </Select>
            </Form.Item>
          </>
        )}
        <Form.Item name="description" label={t('templates.col.description')}>
          <Input />
        </Form.Item>
        <Form.Item name="templateContent" label={t('templates.col.templateContent', { defaultValue: 'Nội dung mẫu' })} rules={[{ required: true }]}>
          <TextArea
            rows={12}
            style={{ fontFamily: 'monospace', fontSize: 13 }}
            placeholder="{{- /* Scriban template */ -}}"
          />
        </Form.Item>
      </Form>
    </Modal>
  );
}

// ─── List page ────────────────────────────────────────────────────────────────

// TemplateListPage — CRUD table for DocumentTemplates with inline editor and preview
export function TemplateListPage() {
  const { t } = useTranslation();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editOpen, setEditOpen] = useState(false);
  const [searchInput, setSearchInput] = useState('');
  const [previewId, setPreviewId] = useState<string | null>(null);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  // Debounce search input by 300ms — prevents API call on every keystroke
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const { antPagination, toQueryParams } = useServerPagination(20, [debouncedSearch]);

  const { data, isFetching } = useTemplates({
    ...toQueryParams(),
    search: debouncedSearch || undefined,
  });
  const deleteMutation = useDeleteTemplate();

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  // Bulk delete — allSettled so partial failures are reported, not silently swallowed
  async function handleBulkDelete() {
    const results = await Promise.allSettled(selectedIds.map(id => deleteMutation.mutateAsync(id)));
    const failed = results.filter(r => r.status === 'rejected').length;
    if (failed > 0) {
      appMessage.warning(t('common.bulkDeletePartialFail', { failed, total: selectedIds.length, defaultValue: `${failed}/${selectedIds.length} mục xóa thất bại` }));
    } else {
      appMessage.success(t('common.bulkDeleteSuccess', { defaultValue: 'Xóa thành công' }));
    }
    setSelectedIds([]);
  }

  const columns: ColumnsType<DocumentTemplateDto> = [
    { title: t('templates.col.name'), dataIndex: 'name', key: 'name', ellipsis: true },
    {
      title: t('templates.col.outputFormat', { defaultValue: 'Định dạng' }),
      dataIndex: 'outputFormat',
      key: 'outputFormat',
      width: 110,
      render: (v: string) => <Tag>{v}</Tag>,
    },
    {
      title: t('templates.col.status', { defaultValue: 'Trạng thái' }),
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (v: string) => <Tag color={v === 'Active' ? 'green' : v === 'Draft' ? 'default' : 'red'}>{v}</Tag>,
    },
    {
      title: t('templates.col.createdAt', { defaultValue: 'Ngày tạo' }),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 130,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY') : '—'),
    },
    {
      title: '',
      key: 'actions',
      width: 150,
      render: (_, record) => (
        <Space size="small">
          <Button size="small" icon={<EditOutlined />} onClick={() => { setEditingId(record.id); setEditOpen(true); }} />
          <Tooltip title={t('templates.previewBtn', 'Xem trước')}>
            <Button size="small" icon={<DownloadOutlined />} onClick={() => setPreviewId(record.id)} />
          </Tooltip>
          <Popconfirm
            title={t('templates.deleteConfirm')}
            onConfirm={() => deleteMutation.mutate(record.id, {
              onSuccess: () => appMessage.success(t('common.deleted', 'Xóa thành công')),
              onError: () => appMessage.error(t('common.error', 'Thao tác thất bại')),
            })}
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
    <div>
      <AdminPageHeader
        title={t('templates.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => { setEditingId(null); setEditOpen(true); }}
          >
            {t('common.add')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchInput}
          onSearchChange={setSearchInput}
        />
        {selectedIds.length > 0 && (
          <Flex gap={8} style={{ padding: '0 24px 8px' }}>
            <Popconfirm
              title={t('templates.deleteConfirm', `Xóa ${selectedIds.length} template đã chọn?`)}
              onConfirm={handleBulkDelete}
              okText={t('common.confirm')}
              cancelText={t('common.cancel')}
            >
              <Button danger size="small">
                {t('common.deleteSelected', `Xóa (${selectedIds.length})`)}
              </Button>
            </Popconfirm>
          </Flex>
        )}
        <Table<DocumentTemplateDto>
          rowKey="id"
          columns={columns}
          dataSource={items}
          loading={isFetching}
          size="small"
          scroll={{ x: 700 }}

          pagination={{ ...antPagination, total }}
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
        />
      </AdminContentCard>

      <EditModal
        templateId={editingId}
        open={editOpen}
        onClose={() => { setEditOpen(false); setEditingId(null); }}
      />

      {previewId && (
        <PreviewModal
          templateId={previewId}
          open={Boolean(previewId)}
          onClose={() => setPreviewId(null)}
        />
      )}
    </div>
  );
}
