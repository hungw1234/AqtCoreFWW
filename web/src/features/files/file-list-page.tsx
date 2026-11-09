import { useState, lazy, Suspense } from 'react';
import { Table, Tag, Button, Upload, Space, Popconfirm, Flex, Tooltip, Progress, Drawer } from 'antd';
import { UploadOutlined, DownloadOutlined, DeleteOutlined, InboxOutlined, SafetyCertificateOutlined, HistoryOutlined, HighlightOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { UploadProps } from 'antd';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useFiles, useUploadFile, useDeleteFile, downloadFile, useFileScanStatus } from './file-api';
import { useUploadSignEnabled } from './file-signing-api';
import { formatFileSize } from './file-types';
import { FileLifecycleSteps, deriveFilePhase } from './file-lifecycle-steps';
import { FileTypeIcon } from './file-type-icon';
import { SignUploadedFileModal } from './sign-uploaded-file-modal';
// Lazy: this modal pulls in react-pdf/pdfjs (placement overlay + signed viewer), which cannot load under
// jsdom (no DOMMatrix) and is heavy at runtime. Deferring it keeps react-pdf out of FileListPage's eager
// import graph — same rationale as SignedPdfViewer being lazy-loaded by the steps panel.
const SignUploadedFilePadesModal = lazy(() =>
  import('./sign-uploaded-file-pades-modal').then((m) => ({ default: m.SignUploadedFilePadesModal })),
);
import { SigningStepsPanel } from '@/features/digital-signature/signed-document/signing-steps-panel';
import { useSignatureOverview } from '@/features/profile/signatures/signatures-api';
import type { EnrollmentKind } from '@/features/profile/signatures/signature-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import type { FileRecordDto, VirusScanStatus } from './file-types';
import { appMessage } from '@/core/api/notification-bridge';

const { Dragger } = Upload;

const SCAN_COLOR: Record<VirusScanStatus, string> = {
  Pending: 'processing',
  Clean: 'success',
  Infected: 'error',
  Failed: 'warning',
};

// FileListPage — paginated file table with upload dragger
export function FileListPage() {
  const { t } = useTranslation();
  const [uploadVisible, setUploadVisible] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [uploadPct, setUploadPct] = useState<number | null>(null);
  const [uploadedFileId, setUploadedFileId] = useState<string | null>(null);
  // Track which PDF row the USB DocFlow signing modal is open for
  const [signingFile, setSigningFile] = useState<FileRecordDto | null>(null);
  // Track which PDF row the remote-PAdES (visible-stamp placement) signing modal is open for
  const [signingPadesFile, setSigningPadesFile] = useState<FileRecordDto | null>(null);
  // Track which row's signing history drawer is open (null = closed)
  const [historyFileId, setHistoryFileId] = useState<string | null>(null);
  // Fetch all data — client-side search needs full dataset
  const { data, isLoading } = useFiles(1, 9999);
  const { mutate: upload, isPending: isUploading } = useUploadFile(setUploadPct);
  // Poll the just-uploaded file's scan status so the lifecycle stepper can advance past "Tải lên".
  const scanStatus = useFileScanStatus(uploadedFileId);
  const deleteMutation = useDeleteFile();

  // Sign-button eligibility — ENROLLMENT-DRIVEN. The user's OWN approved enrollment decides which
  // sign path is offered, independent of the tenant's single active-provider config: the BE
  // /signing/request endpoint resolves the provider purely from the client-supplied key (no
  // tenant-config check), so the provider a user enrolled — not the tenant default — is what
  // actually works. Each button maps to one enrollment kind:
  //   • "Ký số" (USB DocFlow) — needs a UsbTokenVgca enrollment; client-side VGCA, the DocFlow
  //     path runs NO binding guard, so an approved enrollment is sufficient.
  //   • "Ký số có đóng dấu (PAdES)" (remote Mysign) — needs a RemoteViettelMysign enrollment;
  //     server-side, runs SignerBindingGuard which fail-closes (403) otherwise.
  // Both require ACTIVE + Approved: Revoke() clears IsActive but leaves ApprovalStatus = Approved,
  // and the BE binds only active rows, so an Approved-but-inactive enrollment must not unlock.
  const { data: signatureOverview } = useSignatureOverview();
  const enrollments = signatureOverview?.enrollments ?? [];
  const hasActiveApprovedEnrollment = (kind: EnrollmentKind) =>
    enrollments.some((e) => e.kind === kind && e.approvalStatus === 'Approved' && e.isActive);
  const canUsbSign = hasActiveApprovedEnrollment('UsbTokenVgca');
  const canPadesSign = hasActiveApprovedEnrollment('RemoteViettelMysign');
  // Signing an uploaded PDF (both the USB DocFlow and remote-PAdES buttons target a user-uploaded row)
  // is gated deployment-wide: the untrusted-PDF crypto-verify sandbox chain is deferred to a later
  // release, so the BE rejects these requests (409) while the flag is off. When off, HIDE the entries
  // entirely (not disable) — a disabled button would show an enrollment tooltip that misstates the real
  // reason. Enrollment eligibility (canUsbSign/canPadesSign) still governs disabled state when it is on.
  const uploadSignEnabled = useUploadSignEnabled();

  // Collapse the uploader and clear the in-flight lifecycle state (progress + scan tracking).
  function closeUploader() {
    setUploadVisible(false);
    setUploadedFileId(null);
    setUploadPct(null);
  }

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

  // Derived from antd's public surface instead of the deep `rc-upload/lib/interface`
  // import — rc-upload is a transitive (phantom) dependency and pnpm's strict layout
  // does not expose it, which broke the Docker image build (TS2307).
  type UploadRequestOption = Parameters<NonNullable<UploadProps['customRequest']>>[0];

  function handleUpload(options: UploadRequestOption) {
    const formData = new FormData();
    formData.append('file', options.file as File);
    upload(formData, {
      onSuccess: (result) => {
        setUploadPct(null);
        // Keep the uploader open and start tracking the scan so the stepper advances to "Quét virus";
        // the list itself is refreshed by the mutation's own onSuccess.
        setUploadedFileId(result.fileId);
        appMessage.success(t('page.files.uploadSuccess'));
        if (options.onSuccess) options.onSuccess({});
      },
      onError: (err) => {
        setUploadPct(null);
        appMessage.error(t('page.files.uploadError'));
        if (options.onError) options.onError(err as Error);
      },
    });
  }

  // Column dataIndex is camelCase — the list endpoint serializes via System.Text.Json (MVC default
  // camelCase), same as every other API DTO. (The BE handler projects PascalCase anonymous-object
  // properties, but STJ lowercases them on the wire.)
  const columns: ColumnsType<FileRecordDto> = [
    {
      title: t('page.files.col.fileName'),
      dataIndex: 'originalFileName',
      key: 'originalFileName',
      ellipsis: true,
    },
    {
      title: t('page.files.col.contentType'),
      dataIndex: 'contentType',
      key: 'contentType',
      width: 80,
      align: 'center',
      render: (v: string | null | undefined) => <FileTypeIcon contentType={v} />,
    },
    {
      title: t('page.files.col.sizeBytes'),
      dataIndex: 'sizeBytes',
      key: 'sizeBytes',
      width: 110,
      render: (v: number) => formatFileSize(v ?? 0),
    },
    {
      title: t('page.files.col.virusScan'),
      dataIndex: 'virusScanStatus',
      key: 'virusScanStatus',
      width: 120,
      render: (v: VirusScanStatus) => (
        <Tag color={SCAN_COLOR[v] ?? 'default'}>{t(`page.files.scanStatus.${v ?? 'Pending'}`)}</Tag>
      ),
    },
    {
      title: t('page.files.col.uploadedBy'),
      dataIndex: 'uploadedByName',
      key: 'uploadedByName',
      width: 180,
      ellipsis: true,
      render: (v: string | null | undefined) => v ?? '—',
    },
    {
      title: t('page.files.col.uploadedAt'),
      dataIndex: 'uploadedAt',
      key: 'uploadedAt',
      width: 130,
      render: (v: string) => v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—',
    },
    {
      title: t('page.files.isDigitallySigned', 'Đã ký số'),
      dataIndex: 'isDigitallySigned',
      key: 'isDigitallySigned',
      width: 90,
      align: 'center',
      render: (v: boolean | undefined) =>
        v ? (
          <Tag color="blue" icon={<SafetyCertificateOutlined />}>
            {t('digitalSignature.signedDocument.signedBadge', 'Đã ký')}
          </Tag>
        ) : null,
    },
    {
      title: '',
      key: 'actions',
      width: 240,
      render: (_: unknown, r: FileRecordDto) => (
        <Space size="small">
          {/* "Ký số" only for scan-passed PDFs — staging requires an Available file (BE returns 404
              otherwise), so gating on Clean here avoids a dead click while a scan is still pending.
              Disabled (not hidden) when the user has no approved USB-token enrollment, so the gap is
              visible. A disabled antd Button swallows mouse events, so the Tooltip needs the <span>
              wrapper to still surface on hover. */}
          {uploadSignEnabled && r.contentType === 'application/pdf' && r.virusScanStatus === 'Clean' && (
            <Tooltip
              title={
                canUsbSign
                  ? t('page.files.sign', 'Ký số')
                  : t('page.files.signUsbIneligible', 'Cần đăng ký và được duyệt chứng thư USB token (VGCA) để ký số')
              }
            >
              <span>
                <Button
                  size="small"
                  icon={<SafetyCertificateOutlined />}
                  disabled={!canUsbSign}
                  onClick={() => setSigningFile(r)}
                />
              </span>
            </Tooltip>
          )}
          {/* "Ký số có đóng dấu" — remote PAdES with a visible-stamp placement; same scan gate as USB sign.
              Disabled unless the signer has an Approved remote-Mysign enrollment (else the server binding
              guard 403s), with a tooltip pointing at enrollment. */}
          {uploadSignEnabled && r.contentType === 'application/pdf' && r.virusScanStatus === 'Clean' && (
            <Tooltip
              title={
                canPadesSign
                  ? t('page.files.signPades', 'Ký số có đóng dấu (PAdES)')
                  : t(
                      'page.files.signPadesIneligible',
                      'Cần đăng ký và được duyệt chứng thư ký từ xa (Viettel Mysign) để dùng ký có đóng dấu',
                    )
              }
            >
              <span>
                <Button
                  size="small"
                  icon={<HighlightOutlined />}
                  disabled={!canPadesSign}
                  onClick={() => setSigningPadesFile(r)}
                />
              </span>
            </Tooltip>
          )}
          {/* "Lịch sử ký" — only shown for files that have been signed (hasSignedPdf=true somewhere) */}
          {r.isDigitallySigned && (
            <Tooltip title={t('page.files.signingHistory', 'Lịch sử ký')}>
              <Button
                size="small"
                icon={<HistoryOutlined />}
                onClick={() => setHistoryFileId(r.id)}
              />
            </Tooltip>
          )}
          <Tooltip title={t('page.files.download', 'Tải xuống')}>
            <Button size="small" icon={<DownloadOutlined />} onClick={() => downloadFile(r.id, r.originalFileName)} />
          </Tooltip>
          <Popconfirm
            title={t('common.confirm')}
            description={t('common.delete') + '?'}
            onConfirm={() => deleteMutation.mutate(r.id, {
              onSuccess: () => appMessage.success(t('common.deleteSuccess', { defaultValue: 'Xóa thành công' })),
              onError: () => appMessage.error(t('common.deleteFailed', { defaultValue: 'Xóa thất bại' })),
            })}
            okText={t('common.yes')}
            cancelText={t('common.no')}
          >
            <Button size="small" danger icon={<DeleteOutlined />} loading={deleteMutation.isPending} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      {/* Signing modal — rendered outside the table to avoid re-mounting on row re-render */}
      {signingFile && (
        <SignUploadedFileModal
          open={Boolean(signingFile)}
          fileId={signingFile.id}
          originalFileName={signingFile.originalFileName}
          onClose={() => setSigningFile(null)}
        />
      )}

      {/* Remote-PAdES signing with visible-stamp placement — separate modal/state from the USB DocFlow one.
          Wrapped in Suspense because the modal chunk (react-pdf) is lazy-loaded. */}
      {signingPadesFile && (
        <Suspense fallback={null}>
          <SignUploadedFilePadesModal
            open={Boolean(signingPadesFile)}
            fileId={signingPadesFile.id}
            originalFileName={signingPadesFile.originalFileName}
            onClose={() => setSigningPadesFile(null)}
          />
        </Suspense>
      )}

      {/* Signing history drawer — shows signing steps + view/download for the selected file */}
      <Drawer
        title={t('page.files.signingHistory', 'Lịch sử ký')}
        open={Boolean(historyFileId)}
        onClose={() => setHistoryFileId(null)}
        width={640}
        destroyOnHidden
      >
        {historyFileId && (
          <SigningStepsPanel scope="files" id={historyFileId} />
        )}
      </Drawer>

      <AdminPageHeader
        title={t('page.files.title')}
        stats={{ total: data?.totalCount, label: t('common.items') }}
        actions={
          <Button
            type="primary"
            icon={<UploadOutlined />}
            onClick={() => (uploadVisible ? closeUploader() : setUploadVisible(true))}
          >
            {uploadVisible ? t('common.close') : t('page.files.upload')}
          </Button>
        }
      />

      {uploadVisible && (
        <div style={{ marginBottom: 16 }}>
          <Dragger
            name="file"
            multiple={false}
            customRequest={handleUpload}
            disabled={isUploading}
            showUploadList={false}
          >
            <Space direction="vertical" align="center">
              <InboxOutlined style={{ fontSize: 32 }} />
              <p>{t('page.files.dragHint')}</p>
              <p style={{ color: '#888', fontSize: 12 }}>{t('page.files.maxSize')}</p>
            </Space>
          </Dragger>
          {isUploading && (
            <Progress percent={uploadPct ?? 0} size="small" status="active" style={{ marginTop: 8 }} />
          )}
          {/* Lifecycle stepper — visible from upload start through the post-202 virus scan. Unlike the
              form widget, a Rejected file here is NOT auto-dropped: it rests on the error step until the
              user closes the uploader (the list page has no pending submission to protect from a tainted id). */}
          <div style={{ marginTop: 8 }}>
            <FileLifecycleSteps
              phase={deriveFilePhase(isUploading, Boolean(uploadedFileId), scanStatus.data?.status)}
            />
          </div>
        </div>
      )}

      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={setSearchText}
        />
        {selectedIds.length > 0 && (
          <Flex gap={8} style={{ padding: '0 24px 8px' }}>
            <Popconfirm
              title={t('common.bulkDeleteConfirm', { defaultValue: `Xóa ${selectedIds.length} mục đã chọn?` })}
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
        <Table<FileRecordDto>
          rowKey="id"
          columns={columns}
          dataSource={(data?.items ?? []).filter(item =>
            !searchText || Object.values(item).some(v =>
              String(v ?? '').toLowerCase().includes(searchText.toLowerCase())
            )
          )}
          loading={isLoading}
          size="small"
          rowSelection={{
            selectedRowKeys: selectedIds,
            onChange: (keys) => setSelectedIds(keys as string[]),
          }}
          pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (n) => t('page.files.showTotal', { count: n }) }}
          locale={{ emptyText: t('page.files.empty') }}
        />
      </AdminContentCard>
    </div>
  );
}
