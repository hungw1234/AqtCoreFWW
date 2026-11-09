import { useState } from 'react';
import { Table, Tag, Typography, Button, Descriptions, Space, Alert, Row, Col, Input, Modal, Tabs } from 'antd';
import { CenteredSpin } from '@/shared/components/centered-spin';
import { ArrowUpOutlined, ArrowDownOutlined, CopyOutlined, FormOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useParams } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useFormTemplate, usePublishTemplate, useBulkReorderFields, useDuplicateTemplate } from './form-api';
import { FormSubmissionsSection } from './form-submissions-section';
import { FormAnalyticsPanel } from './form-analytics-panel';
import { FormVersionDiffPanel } from './form-version-diff-panel';
import { FormSettingsPanel } from './form-settings-panel';
import { FormSubmitModal } from './form-submit-modal';
import { ViewManager } from './views';
import type { FormFieldDto, FormStatus } from './form-types';
import { appMessage } from '@/core/api/notification-bridge';

const { Title } = Typography;

const TEMPLATE_STATUS_COLOR: Record<FormStatus, string> = {
  Draft: 'default', Materializing: 'blue', Active: 'green', Inactive: 'orange',
};

// FormDetailPage — template details, field list, submissions, analytics, version diff
export function FormDetailPage() {
  const { t } = useTranslation();
  const { id } = useParams({ strict: false }) as { id: string };
  const [duplicateModalOpen, setDuplicateModalOpen] = useState(false);
  const [dupName, setDupName] = useState('');
  const [dupCode, setDupCode] = useState('');
  const [submitOpen, setSubmitOpen] = useState(false);

  const { data: template, isLoading } = useFormTemplate(id);
  const { mutate: publish, isPending: isPublishing } = usePublishTemplate();
  const { mutate: bulkReorder } = useBulkReorderFields(id);
  const { mutate: duplicate, isPending: isDuplicating } = useDuplicateTemplate();

  if (isLoading) return <CenteredSpin tip={t('common.loading')} style={{ display: 'block', marginTop: 48 }} />;
  if (!template) return <Alert type="error" message={t('page.forms.detail.notFound')} />;

  const sortedFields = [...template.fields].sort((a, b) => a.displayOrder - b.displayOrder);

  function swapFieldOrder(fieldId: string, direction: 'up' | 'down') {
    const idx = sortedFields.findIndex((f) => f.id === fieldId);
    if (idx < 0) return;
    const swapIdx = direction === 'up' ? idx - 1 : idx + 1;
    if (swapIdx < 0 || swapIdx >= sortedFields.length) return;
    const current = sortedFields[idx];
    const target = sortedFields[swapIdx];
    bulkReorder([
      { fieldId: current.id, newOrder: target.displayOrder },
      { fieldId: target.id, newOrder: current.displayOrder },
    ], {
      onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
    });
  }

  const fieldColumns: ColumnsType<FormFieldDto> = [
    { title: t('page.forms.detail.col.order'), dataIndex: 'displayOrder', key: 'displayOrder', width: 80 },
    { title: t('page.forms.detail.col.name'), dataIndex: 'fieldKey', key: 'fieldKey' },
    { title: t('page.forms.detail.col.label'), dataIndex: 'labelVi', key: 'labelVi' },
    { title: t('page.forms.detail.col.fieldType'), dataIndex: 'type', key: 'type', width: 100 },
    {
      title: t('page.forms.detail.col.isRequired'), dataIndex: 'required', key: 'required', width: 90,
      render: (v: boolean) => <Tag color={v ? 'red' : 'default'}>{v ? t('common.yes') : t('common.no')}</Tag>,
    },
    {
      title: t('page.forms.detail.col.reorder'), key: 'order-actions', width: 80,
      render: (_, record) => {
        const idx = sortedFields.findIndex((f) => f.id === record.id);
        return (
          <Space size={2}>
            <Button size="small" icon={<ArrowUpOutlined />} disabled={idx <= 0}
              onClick={() => swapFieldOrder(record.id, 'up')} />
            <Button size="small" icon={<ArrowDownOutlined />} disabled={idx >= sortedFields.length - 1}
              onClick={() => swapFieldOrder(record.id, 'down')} />
          </Space>
        );
      },
    },
  ];

  const tabItems = [
    {
      key: 'fields',
      label: t('page.forms.detail.fieldsSection', { defaultValue: 'Fields' }),
      children: (
        <Table<FormFieldDto>
          rowKey="id" columns={fieldColumns} dataSource={sortedFields}
          size="small" pagination={false} scroll={{ x: 600 }} />
      ),
    },
    {
      key: 'submissions',
      label: t('page.forms.detail.submissionsSection', { count: template.submissionsCount, defaultValue: `Submissions (${template.submissionsCount})` }),
      children: (
        <FormSubmissionsSection templateId={id} fields={template.fields} />
      ),
    },
    {
      key: 'analytics',
      label: t('page.forms.detail.analyticsTab'),
      children: <FormAnalyticsPanel templateId={id} />,
    },
    {
      key: 'diff',
      label: t('page.forms.detail.versionDiffTab'),
      children: (
        <FormVersionDiffPanel
          templateId={id} currentVersion={template.version ?? 1} />
      ),
    },
    {
      key: 'settings',
      label: t('forms.settings.title', { defaultValue: 'Settings' }),
      children: <FormSettingsPanel template={template} />,
    },
    {
      key: 'views',
      label: t('page.forms.viewsTab', { defaultValue: 'Views' }),
      children: <ViewManager templateId={id} fields={template.fields} />,
    },
  ];

  return (
    <div>
      <Row justify="space-between" align="middle" style={{ marginBottom: 16 }} gutter={[8, 8]}>
        <Col>
          <Title level={4} style={{ margin: 0 }}>{template.name}</Title>
        </Col>
        <Col>
          <Space>
            <Tag color={TEMPLATE_STATUS_COLOR[template.status]}>{template.status}</Tag>
            {template.status === 'Draft' && (
              <Button type="primary" loading={isPublishing} onClick={() => publish(id, {
                onSuccess: () => appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' })),
                onError: () => appMessage.error(t('common.operationFailed', { defaultValue: 'Thao tác thất bại' })),
              })}>
                {t('page.forms.detail.publishBtn')}
              </Button>
            )}
            {template.status === 'Active' && (
              <Button type="primary" icon={<FormOutlined />} onClick={() => setSubmitOpen(true)}>
                {t('page.forms.submitForm', { defaultValue: 'Submit Form' })}
              </Button>
            )}
            <Button icon={<CopyOutlined />} loading={isDuplicating}
              onClick={() => { setDupName(`${template.name} (copy)`); setDupCode(`${template.code}_copy`); setDuplicateModalOpen(true); }}>
              {t('page.forms.detail.duplicateBtn', { defaultValue: 'Duplicate' })}
            </Button>
          </Space>
        </Col>
      </Row>

      <Descriptions size="small" bordered style={{ marginBottom: 24 }}>
        <Descriptions.Item label={t('page.forms.col.code')}>{template.code}</Descriptions.Item>
        <Descriptions.Item label={t('page.forms.col.submissionsCount')}>{template.submissionsCount}</Descriptions.Item>
        <Descriptions.Item label={t('page.forms.col.createdAt')}>{dayjs(template.createdAt).format('DD/MM/YYYY')}</Descriptions.Item>
      </Descriptions>

      <Tabs items={tabItems} defaultActiveKey="fields" />

      <Modal open={duplicateModalOpen}
        title={t('page.forms.detail.duplicateTitle', { defaultValue: 'Duplicate Template' })}
        onOk={() => duplicate(
          { id, name: dupName, code: dupCode },
          {
            onSuccess: () => { setDuplicateModalOpen(false); appMessage.success(t('page.forms.detail.duplicateSuccess', { defaultValue: 'Template duplicated' })); },
            onError: () => appMessage.error(t('page.forms.detail.duplicateFailed')),
          }
        )}
        onCancel={() => setDuplicateModalOpen(false)}
        okText={t('page.forms.detail.duplicateConfirmBtn', { defaultValue: 'Duplicate' })}
        okButtonProps={{ disabled: !dupName.trim() || !dupCode.trim(), loading: isDuplicating }}
        destroyOnHidden>
        <Space direction="vertical" style={{ width: '100%' }}>
          <Input placeholder={t('page.forms.detail.duplicateNameLabel', { defaultValue: 'New name' })}
            value={dupName} onChange={(e) => setDupName(e.target.value)} />
          <Input placeholder={t('page.forms.detail.duplicateCodeLabel', { defaultValue: 'New code (slug)' })}
            value={dupCode} onChange={(e) => setDupCode(e.target.value)} />
        </Space>
      </Modal>

      <FormSubmitModal template={template} open={submitOpen} onClose={() => setSubmitOpen(false)} />
    </div>
  );
}
