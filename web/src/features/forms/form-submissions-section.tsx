// Form submissions section — table with bulk select, approve/reject, PDF download, field filters
import { useState, useMemo } from 'react';
import {
  Table,
  Tag,
  Button,
  Space,
  Popconfirm,
  Input,
  Modal,
  Row,
  Col,
  Select,
} from 'antd';
import { CheckOutlined, CloseOutlined, DownloadOutlined, FilePdfOutlined, PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import {
  useFormSubmissions, useApproveSubmission, useRejectSubmission,
  useBulkApproveSubmissions, useBulkRejectSubmissions,
  useExportSubmissions, useExportSubmissionPdf,
} from './form-api';
import type { FormSubmissionListItemDto, SubmissionStatus, SubmissionFieldFilter, FormFieldDto } from './form-types';
import { useDebouncedValue } from '@/core/hooks/use-debounced-value';
import { appMessage } from '@/core/api/notification-bridge';
import { SignSubmissionDocumentButton, isSubmissionPdfEligible } from './sign-submission-document-button';

const STATUS_COLOR: Record<SubmissionStatus, string> = {
  Pending: 'gold', Approved: 'green', Rejected: 'red',
};

interface Props {
  templateId: string;
  fields?: FormFieldDto[];
}

export function FormSubmissionsSection({ templateId, fields = [] }: Props) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [rejectModalOpen, setRejectModalOpen] = useState(false);
  const [rejectTarget, setRejectTarget] = useState<{ submissionId: string } | null>(null);
  const [rejectComment, setRejectComment] = useState('');
  const [bulkRejectOpen, setBulkRejectOpen] = useState(false);
  const [bulkRejectComment, setBulkRejectComment] = useState('');
  const [fieldFilters, setFieldFilters] = useState<SubmissionFieldFilter[]>([]);

  // Debounce field filter values — prevent API call on every keystroke
  const debouncedFilters = useDebouncedValue(fieldFilters, 400);
  // Only send filters that have both fieldKey and value filled
  const activeFilters = useMemo(
    () => debouncedFilters.filter((f) => f.fieldKey && f.value),
    [debouncedFilters],
  );

  const { data: submissions } = useFormSubmissions(templateId, page, 20, statusFilter, activeFilters.length ? activeFilters : undefined);
  const { mutate: approve, isPending: isApproving } = useApproveSubmission(templateId);
  const { mutate: reject, isPending: isRejecting } = useRejectSubmission(templateId);
  const { mutate: bulkApprove, isPending: isBulkApproving } = useBulkApproveSubmissions(templateId);
  const { mutate: bulkReject, isPending: isBulkRejecting } = useBulkRejectSubmissions(templateId);
  const { mutate: exportCsv, isPending: isExporting } = useExportSubmissions();
  const { mutate: exportPdf, isPending: isExportingPdf } = useExportSubmissionPdf();

  const columns: ColumnsType<FormSubmissionListItemDto> = [
    { title: 'ID', dataIndex: 'id', key: 'id', ellipsis: true, width: 120 },
    { title: t('page.forms.detail.col.submittedBy'), dataIndex: 'submittedBy', key: 'submittedBy' },
    {
      title: t('page.forms.detail.col.submittedAt'), dataIndex: 'submittedAt', key: 'submittedAt',
      width: 130, render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
    {
      title: t('page.forms.detail.col.status'), dataIndex: 'status', key: 'status', width: 100,
      render: (v: SubmissionStatus) => <Tag color={STATUS_COLOR[v] ?? 'default'}>{v}</Tag>,
    },
    {
      title: t('page.forms.detail.col.actions'), key: 'actions', width: 200,
      render: (_: unknown, record: FormSubmissionListItemDto) => (
        <Space size={4}>
          {record.status === 'Pending' && (
            <>
              <Popconfirm
                title={t('page.forms.detail.submission.approveConfirm')}
                onConfirm={() => approve({ submissionId: record.id }, {
                  onSuccess: () => appMessage.success(t('page.forms.detail.submission.approveSuccessMsg', { defaultValue: 'Duyệt thành công' })),
                  onError: () => appMessage.error(t('page.forms.detail.submission.approveFailedMsg')),
                })}
                okText={t('common.yes')} cancelText={t('common.no')}
              >
                <Button size="small" type="primary" icon={<CheckOutlined />} loading={isApproving} />
              </Popconfirm>
              <Button size="small" danger icon={<CloseOutlined />} loading={isRejecting}
                onClick={() => { setRejectTarget({ submissionId: record.id }); setRejectComment(''); setRejectModalOpen(true); }} />
            </>
          )}
          <Button size="small" icon={<FilePdfOutlined />} loading={isExportingPdf}
            onClick={() => exportPdf({ submissionId: record.id },
              { onError: () => appMessage.error(t('page.forms.detail.submission.pdfExportFailedMsg')) })} />
          {/* Sign document button — visible only for PDF-eligible (Approved) submissions */}
          {isSubmissionPdfEligible(record.status) && (
            <SignSubmissionDocumentButton
              publicId={record.id}
              templateId={templateId}
              status={record.status}
              iconOnly
            />
          )}
        </Space>
      ),
    },
  ];

  function handleBulkApprove() {
    bulkApprove({ ids: selectedIds },
      {
        onSuccess: (r) => { appMessage.success(t('page.forms.detail.submission.bulkApproveSuccessMsg', { count: r.succeeded })); setSelectedIds([]); },
        onError: () => appMessage.error(t('page.forms.detail.submission.bulkApproveFailedMsg')),
      });
  }

  return (
    <>
      {/* Filter bar */}
      <Row gutter={8} style={{ marginBottom: 8 }}>
        <Col>
          <Select allowClear placeholder={t('page.forms.detail.submission.filterByStatus')} style={{ width: 160 }}
            onChange={(v) => { setStatusFilter(v); setPage(1); }}
            options={[
              { value: 'Pending', label: t('page.forms.detail.submission.statusPending') },
              { value: 'Approved', label: t('page.forms.detail.submission.statusApproved') },
              { value: 'Rejected', label: t('page.forms.detail.submission.statusRejected') },
            ]}
          />
        </Col>
        {/* Field filters */}
        {fieldFilters.map((filter, idx) => (
          <Col key={idx}>
            <Space size={4}>
              <Select
                size="small" style={{ width: 120 }}
                placeholder={t('page.forms.filterField', { defaultValue: 'Field' })}
                value={filter.fieldKey || undefined}
                onChange={(v) => {
                  const updated = [...fieldFilters];
                  updated[idx] = { ...updated[idx], fieldKey: v };
                  setFieldFilters(updated);
                }}
                options={fields
                  .filter((f) => !['Section', 'Label', 'Divider'].includes(f.type))
                  .map((f) => ({ value: f.fieldKey, label: f.labelVi || f.fieldKey }))}
              />
              <Input
                size="small" style={{ width: 100 }}
                placeholder={t('page.forms.filterValue', { defaultValue: 'Value' })}
                value={filter.value}
                onChange={(e) => {
                  const updated = [...fieldFilters];
                  updated[idx] = { ...updated[idx], value: e.target.value };
                  setFieldFilters(updated);
                }}
                onPressEnter={() => setPage(1)}
              />
              <Button size="small" type="text" danger icon={<DeleteOutlined />}
                onClick={() => setFieldFilters((prev) => prev.filter((_, i) => i !== idx))} />
            </Space>
          </Col>
        ))}
        <Col>
          <Button size="small" type="dashed" icon={<PlusOutlined />}
            onClick={() => setFieldFilters((prev) => [...prev, { fieldKey: '', value: '' }])}>
            {t('page.forms.addFilter', { defaultValue: 'Filter' })}
          </Button>
        </Col>
        <Col flex="auto" />
        {selectedIds.length > 0 && (
          <>
            <Col>
              <Popconfirm title={t('page.forms.detail.submission.bulkApproveConfirm', { count: selectedIds.length })}
                onConfirm={handleBulkApprove} okText={t('common.yes')} cancelText={t('common.no')}>
                <Button size="small" type="primary" loading={isBulkApproving}>
                  {t('page.forms.detail.submission.bulkApprove', { count: selectedIds.length })}
                </Button>
              </Popconfirm>
            </Col>
            <Col>
              <Button size="small" danger loading={isBulkRejecting}
                onClick={() => { setBulkRejectComment(''); setBulkRejectOpen(true); }}>
                {t('page.forms.detail.submission.bulkReject', { count: selectedIds.length })}
              </Button>
            </Col>
          </>
        )}
        <Col>
          <Button size="small" icon={<DownloadOutlined />} loading={isExporting}
            disabled={!submissions?.totalCount}
            onClick={() => exportCsv(templateId, { onError: () => appMessage.error(t('page.forms.detail.submission.exportFailedMsg')) })}>
            {t('page.forms.detail.submission.exportCsv')}
          </Button>
        </Col>
      </Row>

      <Table<FormSubmissionListItemDto>
        rowKey="id"
        rowSelection={{ selectedRowKeys: selectedIds, onChange: (keys) => setSelectedIds(keys as string[]) }}
        columns={columns}
        dataSource={submissions?.items ?? []}
        size="small"
        scroll={{ x: 700 }}
        pagination={{
          current: page, pageSize: 20, total: submissions?.totalCount,
          onChange: setPage, showSizeChanger: false,
        }}
        locale={{ emptyText: t('page.forms.detail.noSubmissions') }}
      />

      {/* Single reject modal */}
      <Modal open={rejectModalOpen} title={t('page.forms.detail.submission.rejectTitle')}
        onOk={() => {
          if (!rejectTarget) return;
          reject({ submissionId: rejectTarget.submissionId, comment: rejectComment },
            { onSuccess: () => setRejectModalOpen(false), onError: () => appMessage.error(t('page.forms.detail.submission.rejectFailedMsg')) });
        }}
        onCancel={() => setRejectModalOpen(false)}
        okText={t('page.forms.detail.submission.rejectBtn')}
        okButtonProps={{ danger: true, disabled: !rejectComment.trim(), loading: isRejecting }}
        destroyOnHidden>
        <Input.TextArea rows={3} value={rejectComment}
          onChange={(e) => setRejectComment(e.target.value)}
          placeholder={t('page.forms.detail.submission.rejectReasonPlaceholder')} />
      </Modal>

      {/* Bulk reject modal */}
      <Modal open={bulkRejectOpen}
        title={t('page.forms.detail.submission.bulkRejectTitle', { count: selectedIds.length })}
        onOk={() => {
          bulkReject({ ids: selectedIds, comment: bulkRejectComment },
            {
              onSuccess: (r) => { appMessage.success(t('page.forms.detail.submission.bulkRejectSuccessMsg', { count: r.succeeded })); setSelectedIds([]); setBulkRejectOpen(false); },
              onError: () => appMessage.error(t('page.forms.detail.submission.bulkRejectFailedMsg')),
            });
        }}
        onCancel={() => setBulkRejectOpen(false)}
        okText={t('page.forms.detail.submission.rejectAllBtn')}
        okButtonProps={{ danger: true, disabled: !bulkRejectComment.trim(), loading: isBulkRejecting }}
        destroyOnHidden>
        <Input.TextArea rows={3} value={bulkRejectComment}
          onChange={(e) => setBulkRejectComment(e.target.value)}
          placeholder={t('page.forms.detail.submission.rejectReasonPlaceholder')} />
      </Modal>
    </>
  );
}
