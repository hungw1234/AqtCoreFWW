/**
 * signing-steps-panel.tsx — Antd table listing signing steps for a given artifact.
 *
 * Consumed by both the Files and Forms features via `scope` prop — no duplication.
 * Each row shows: sequence, signer fallback, completedAt, status badge, and
 * "Xem" (open embedded viewer) / "Tải về" (blob download) actions gated on hasSignedPdf.
 *
 * signerName is not yet resolved server-side, so the panel shows a readable fallback
 * derived from the sequence number ("Người ký #N") — avoids a user-lookup round-trip.
 */

import { useState, lazy, Suspense } from 'react';
import { Table, Tag, Button, Space, Alert, Spin, Typography, Empty } from 'antd';
import { EyeOutlined, DownloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useSigningSteps } from './use-signing-steps';
import { fetchSignedStepBlob } from './signed-document-api';
import { appMessage } from '@/core/api/notification-bridge';
import { extractApiErrorMessage } from '@/core/api/extract-api-error';
import type { SigningStepRow, SignedDocumentScope, SigningStepStatus } from './signing-step-types';

// Lazy-load the PDF viewer so react-pdf + pdfjs do not inflate the initial bundle.
// The viewer is only needed when the user explicitly clicks "Xem" on a completed step.
const SignedPdfViewer = lazy(() =>
  import('./signed-pdf-viewer').then((m) => ({ default: m.SignedPdfViewer })),
);

const { Text } = Typography;

// ─── Status badge colours ─────────────────────────────────────────────────────

const STATUS_COLOR: Record<SigningStepStatus, string> = {
  Pending:    'default',
  InProgress: 'processing',
  Completed:  'success',
  Failed:     'error',
  Expired:    'warning',
};

// ─── Props ────────────────────────────────────────────────────────────────────

export interface SigningStepsPanelProps {
  /** 'files' or 'forms' — selects the API URL prefix. */
  scope: SignedDocumentScope;
  /** Files fileId (publicId) or Forms submissionPublicId. */
  id: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export function SigningStepsPanel({ scope, id }: SigningStepsPanelProps) {
  const { t } = useTranslation();
  const { data: steps = [], isLoading, error } = useSigningSteps(scope, id);

  // Viewer state — blob + open flag + title
  const [viewerBlob, setViewerBlob] = useState<Blob | null>(null);
  const [viewerTitle, setViewerTitle] = useState<string>('');
  const [viewerOpen, setViewerOpen] = useState(false);
  const [fetchingId, setFetchingId] = useState<string | null>(null);

  // ── Helpers ──────────────────────────────────────────────────────────────────

  function signerLabel(row: SigningStepRow): string {
    if (row.signerName) return row.signerName;
    // signerName is not yet resolved server-side — use a readable fallback to avoid showing raw UUIDs.
    return t(
      'digitalSignature.signedDocument.signerFallback',
      'Người ký #{{seq}}',
      { seq: row.sequenceNumber },
    );
  }

  function statusLabel(status: SigningStepStatus): string {
    const key = `digitalSignature.signedDocument.status${status}` as const;
    const fallbacks: Record<SigningStepStatus, string> = {
      Pending:    'Chờ ký',
      InProgress: 'Đang ký',
      Completed:  'Đã ký',
      Failed:     'Thất bại',
      Expired:    'Hết hạn',
    };
    return t(key, fallbacks[status]);
  }

  // ── View action — fetch blob then open viewer ────────────────────────────────

  async function handleView(row: SigningStepRow) {
    setFetchingId(row.signingRequestId);
    try {
      const blob = await fetchSignedStepBlob(scope, id, row);
      setViewerBlob(blob);
      setViewerTitle(
        `${t('digitalSignature.signedDocument.stepSequence', 'Bước')} ${row.sequenceNumber} — ${t('digitalSignature.signedDocument.viewerTitle', 'Xem tài liệu đã ký')}`,
      );
      setViewerOpen(true);
    } catch (err) {
      const msg =
        extractApiErrorMessage(err) ??
        t('digitalSignature.signedDocument.downloadError', 'Tải tài liệu đã ký thất bại');
      appMessage.error(msg);
    } finally {
      setFetchingId(null);
    }
  }

  // ── Download action — fetch blob then trigger browser save ──────────────────

  async function handleDownload(row: SigningStepRow) {
    setFetchingId(row.signingRequestId);
    try {
      const blob = await fetchSignedStepBlob(scope, id, row);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      // Include a short attempt id so re-signs of the same file (same sequence) save distinct files.
      anchor.download = `signed-step-${row.sequenceNumber}-${row.signingRequestId.slice(0, 8)}.pdf`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      // Revoke immediately — the browser has already started the download by now.
      URL.revokeObjectURL(url);
    } catch (err) {
      const msg =
        extractApiErrorMessage(err) ??
        t('digitalSignature.signedDocument.downloadError', 'Tải tài liệu đã ký thất bại');
      appMessage.error(msg);
    } finally {
      setFetchingId(null);
    }
  }

  // ── Columns ──────────────────────────────────────────────────────────────────

  const columns: ColumnsType<SigningStepRow> = [
    {
      title: t('digitalSignature.signedDocument.stepSequence', 'Bước'),
      dataIndex: 'sequenceNumber',
      key: 'sequenceNumber',
      width: 60,
      align: 'center',
    },
    {
      title: t('digitalSignature.signedDocument.stepSigner', 'Người ký'),
      key: 'signer',
      render: (_: unknown, row: SigningStepRow) => (
        <Text>{signerLabel(row)}</Text>
      ),
    },
    {
      title: t('digitalSignature.signedDocument.stepSignedAt', 'Thời gian ký'),
      dataIndex: 'completedAt',
      key: 'completedAt',
      width: 150,
      render: (v: string | null) =>
        v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—',
    },
    {
      title: t('digitalSignature.signedDocument.stepStatus', 'Trạng thái'),
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (v: SigningStepStatus) => (
        <Tag color={STATUS_COLOR[v] ?? 'default'}>{statusLabel(v)}</Tag>
      ),
    },
    {
      title: '',
      key: 'actions',
      width: 120,
      render: (_: unknown, row: SigningStepRow) => (
        <Space size={4}>
          {/* "Xem" — opens the embedded PDF viewer; only when a signed PDF is available */}
          <Button
            size="small"
            icon={<EyeOutlined />}
            disabled={!row.hasSignedPdf}
            loading={fetchingId === row.signingRequestId}
            onClick={() => handleView(row)}
          >
            {t('digitalSignature.signedDocument.viewBtn', 'Xem')}
          </Button>
          {/* "Tải về" — blob download via bearer; only when a signed PDF is available */}
          <Button
            size="small"
            icon={<DownloadOutlined />}
            disabled={!row.hasSignedPdf}
            loading={fetchingId === row.signingRequestId}
            onClick={() => handleDownload(row)}
          >
            {t('digitalSignature.signedDocument.downloadBtn', 'Tải về')}
          </Button>
        </Space>
      ),
    },
  ];

  // ── Render ───────────────────────────────────────────────────────────────────

  if (isLoading) {
    return (
      <Space style={{ justifyContent: 'center', width: '100%', padding: '16px 0' }}>
        <Spin size="small" />
      </Space>
    );
  }

  if (error) {
    return (
      <Alert
        type="error"
        showIcon
        message={t('digitalSignature.signedDocument.loadError', 'Không tải được lịch sử ký')}
      />
    );
  }

  return (
    <>
      <Table<SigningStepRow>
        rowKey="signingRequestId"
        columns={columns}
        dataSource={[...steps].sort(
          (a, b) =>
            a.sequenceNumber - b.sequenceNumber ||
            // Same sequence (independent re-signs): chronological by completion time, nulls last.
            (a.completedAt ?? '').localeCompare(b.completedAt ?? ''),
        )}
        size="small"
        pagination={false}
        locale={{
          emptyText: (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={t('digitalSignature.signedDocument.noSteps', 'Chưa có bước ký')}
            />
          ),
        }}
      />

      {/* Lazy-loaded PDF viewer — only mounts when viewerOpen=true */}
      <Suspense fallback={null}>
        <SignedPdfViewer
          open={viewerOpen}
          blob={viewerBlob}
          title={viewerTitle}
          onClose={() => {
            setViewerOpen(false);
            setViewerBlob(null);
          }}
        />
      </Suspense>
    </>
  );
}
