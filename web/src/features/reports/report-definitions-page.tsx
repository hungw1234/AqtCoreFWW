import { useState } from 'react';
import { Table, Button, Tag, Space, Modal, Form, Input, Select, Alert, Tooltip } from 'antd';
import { PlusOutlined, PlayCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useReportDefinitions, useCreateReportDefinition } from './report-api';
import { ReportRunModal } from './report-run-modal';
import type { ReportDefinitionDto, CreateReportDefinitionRequest, OutputFormat } from './report-types';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { appMessage } from '@/core/api/notification-bridge';

// ReportDefinitionsPage — list report templates; admin can create; any user can run
export function ReportDefinitionsPage() {
  const { t } = useTranslation();
  const { data: definitions = [], isLoading } = useReportDefinitions();
  const { mutate: createDef, isPending: isCreating } = useCreateReportDefinition();

  const [searchText, setSearchText] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [runOpen, setRunOpen] = useState(false);
  const [runExecutionId, setRunExecutionId] = useState<string | null>(null);
  const [createError, setCreateError] = useState<string | null>(null);
  const [createForm] = Form.useForm<CreateReportDefinitionRequest>();

  function handleCreate(values: CreateReportDefinitionRequest) {
    setCreateError(null);
    createDef(values, {
      onSuccess: () => {
        appMessage.success(t('page.reports.definitions.createSuccess', 'Tạo mẫu báo cáo thành công'));
        createForm.resetFields();
        setCreateOpen(false);
      },
      onError: (err: unknown) => {
        const errMsg = err instanceof Error ? err.message : t('page.reports.definitions.createError');
        setCreateError(errMsg);
        appMessage.error(errMsg);
      },
    });
  }

  const COLUMNS: ColumnsType<ReportDefinitionDto> = [
    {
      title: t('page.reports.definitions.colName'),
      key: 'name',
      render: (_, r) => r.nameVi || r.name,
      ellipsis: true,
    },
    { title: t('page.reports.definitions.colDescription'), dataIndex: 'description', key: 'description', ellipsis: true },
    {
      title: t('page.reports.definitions.colFormat'),
      dataIndex: 'outputFormat',
      key: 'outputFormat',
      width: 100,
      render: (v: OutputFormat) => (
        <Tag color={v === 'Excel' ? 'green' : 'red'}>{v}</Tag>
      ),
    },
    {
      title: t('page.reports.definitions.colStatus'),
      dataIndex: 'isActive',
      key: 'isActive',
      width: 100,
      render: (v: boolean) => (
        <Tag color={v ? 'blue' : 'default'}>
          {v ? t('page.reports.definitions.statusActive') : t('page.reports.definitions.statusInactive')}
        </Tag>
      ),
    },
    {
      title: t('page.reports.definitions.colCreatedAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 120,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY'),
    },
    {
      title: '',
      key: 'actions',
      width: 120,
      render: () => (
        <Tooltip title={t('page.reports.definitions.btnRun', 'Chạy')}>
          <Button size="small" icon={<PlayCircleOutlined />} onClick={() => setRunOpen(true)} />
        </Tooltip>
      ),
    },
  ];

  return (
    <>
      <AdminPageHeader
        title={t('page.reports.definitions.title')}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
            {t('page.reports.definitions.btnCreate')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        <Table<ReportDefinitionDto>
          rowKey="id"
          columns={COLUMNS}
          dataSource={definitions.filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
          locale={{ emptyText: t('page.reports.definitions.empty') }}
        />
      </AdminContentCard>

      {/* Create definition modal */}
      <Modal
        title={t('page.reports.definitions.modalCreateTitle')}
        open={createOpen}
        onCancel={() => { setCreateOpen(false); setCreateError(null); createForm.resetFields(); }}
        onOk={() => createForm.submit()}
        confirmLoading={isCreating}
        okText={t('page.reports.definitions.btnCreate')}
        cancelText={t('common.cancel')}
        width={600}
        destroyOnHidden
      >
        {createError && <Alert type="error" message={createError} showIcon style={{ marginBottom: 16 }} />}
        <Form form={createForm} layout="vertical" onFinish={handleCreate}>
          <Space style={{ width: '100%' }} direction="vertical" size={0}>
            <Form.Item name="name" label={t('page.reports.definitions.fieldNameEn')} rules={[{ required: true }]}>
              <Input placeholder="monthly_case_report" />
            </Form.Item>
            <Form.Item name="nameVi" label={t('page.reports.definitions.fieldNameVi')} rules={[{ required: true }]}>
              <Input placeholder="Báo cáo hồ sơ hàng tháng" />
            </Form.Item>
            <Form.Item name="description" label={t('page.reports.definitions.colDescription')}>
              <Input.TextArea rows={2} />
            </Form.Item>
            <Form.Item name="sqlTemplate" label="SQL Template" rules={[{ required: true }]}>
              <Input.TextArea rows={4} style={{ fontFamily: 'monospace' }} placeholder="SELECT ... FROM cases WHERE ..." />
            </Form.Item>
            <Form.Item name="parametersSchema" label="Parameters Schema (JSON)" initialValue="{}">
              <Input.TextArea rows={2} style={{ fontFamily: 'monospace' }} placeholder='{"fromDate": "string", "toDate": "string"}' />
            </Form.Item>
            <Form.Item name="outputFormat" label={t('page.reports.definitions.colFormat')} rules={[{ required: true }]} initialValue="Excel">
              <Select options={[{ value: 'Excel', label: 'Excel (.xlsx)' }, { value: 'Pdf', label: 'PDF' }]} />
            </Form.Item>
          </Space>
        </Form>
      </Modal>

      {/* Run modal — any definition */}
      <ReportRunModal
        open={runOpen}
        definitions={definitions}
        onClose={() => setRunOpen(false)}
        onExecutionStarted={(id) => {
          setRunOpen(false);
          setRunExecutionId(id);
        }}
      />

      {/* Show newly started execution id as notification hint */}
      {runExecutionId && (
        <Alert
          type="info"
          showIcon
          message={t('page.reports.definitions.runStartedHint', { id: runExecutionId })}
          closable
          onClose={() => setRunExecutionId(null)}
          style={{ marginTop: 16 }}
        />
      )}
    </>
  );
}
