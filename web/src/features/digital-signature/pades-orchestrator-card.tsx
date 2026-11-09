/**
 * pades-orchestrator-card.tsx — DEV-only card on the signature demo page.
 *
 * Drives the FULL PRODUCTION remote-PAdES path end-to-end (no dev shortcut): upload a PDF to Files → wait for the
 * virus scan → create a `/signing/request` on the remote Mysign strategy → confirm the OTP on the phone → a
 * server-side job assembles the signed PDF → download it. Every API call is the real production endpoint; only the
 * card UI is dev-gated as the E2E vehicle.
 */

import { useState, useEffect, useRef } from 'react';
import { Alert, Button, Card, Segmented, Space, Steps, Typography, Upload } from 'antd';
import { UploadOutlined, FilePdfOutlined, EyeOutlined, DownloadOutlined } from '@ant-design/icons';
import type { RcFile } from 'antd/es/upload';
import { useTranslation } from 'react-i18next';
import { extractApiErrorMessage } from '@/core/api/extract-api-error';
import { useUploadFile, useFileScanStatus } from '@/features/files/file-api';
import { useSignUploadedFilePades } from '@/features/files/use-sign-uploaded-file-pades';
import { useSigningRequestStatus, downloadSignedPdf } from './signature-api';
import { buildPadesSteps } from './pades-orchestrator-steps';
import type { SignatureFlowMode } from './signature-types';
import { SignedPdfViewer } from './signed-document/signed-pdf-viewer';

/** Demo-card flow choice: 'tenant' = send no override (BE uses TenantSignatureConfig); else force the flow. */
type FlowChoice = 'tenant' | SignatureFlowMode;

const { Paragraph } = Typography;

function downloadBlob(blob: Blob, name: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

export function PadesOrchestratorCard() {
  const { t } = useTranslation();
  const [file, setFile] = useState<File | null>(null);
  const [fileId, setFileId] = useState<string | null>(null);
  const [signedBlob, setSignedBlob] = useState<Blob | null>(null);
  const [viewerOpen, setViewerOpen] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [flowChoice, setFlowChoice] = useState<FlowChoice>('tenant');
  const flowChoiceRef = useRef<FlowChoice>('tenant'); // read inside the scan-effect without re-arming it on change
  flowChoiceRef.current = flowChoice;
  const startedRef = useRef(false); // guards startSigning to fire once per scan-Available
  const fetchedRef = useRef(false); // guards the signed-PDF download to fire once per Completed

  const upload = useUploadFile();
  const scan = useFileScanStatus(fileId);
  const { startSigning, requestId, error: padesError, isLoading: padesLoading, reset } = useSignUploadedFilePades();
  const reqStatus = useSigningRequestStatus(requestId);

  const scanStatus = scan.data?.status;
  const signStatus = reqStatus.data?.status;

  // Once the upload scan passes, kick off the remote-PAdES signing request (the OTP fires on the phone).
  useEffect(() => {
    if (fileId && scanStatus === 'Available' && !startedRef.current) {
      startedRef.current = true;
      const choice = flowChoiceRef.current;
      void startSigning({
        fileId,
        documentName: file?.name,
        signFlowMode: choice === 'tenant' ? null : choice, // 'tenant' → BE uses TenantSignatureConfig
      });
    }
    if (scanStatus === 'Rejected') {
      setLocalError(t('digitalSignature.padesOrchRejected', 'File bị từ chối ở bước quét virus.'));
    }
  }, [fileId, scanStatus, file, startSigning, t]);

  // Once the signing request reaches Completed, the server-side job has assembled the PDF — fetch it into state so
  // the success panel's View/Download buttons are ready. Do NOT auto-download: let the user choose when to save.
  useEffect(() => {
    if (signStatus === 'Completed' && requestId && !fetchedRef.current) {
      fetchedRef.current = true;
      downloadSignedPdf(requestId)
        .then((blob) => {
          setSignedBlob(blob);
        })
        .catch((e) =>
          setLocalError(
            extractApiErrorMessage(e) ?? t('digitalSignature.padesOrchDownloadFail', 'Tải PDF đã ký thất bại'),
          ),
        );
    }
    // Failed (provider rejected) and Expired (TTL elapsed before completion) are both terminal non-success states.
    if (signStatus === 'Failed' || signStatus === 'Expired') {
      setLocalError(t('digitalSignature.padesOrchSignFail', 'Ký số thất bại (nhà cung cấp từ chối, hết hạn, hoặc lỗi).'));
    }
  }, [signStatus, requestId, t]);

  function handleStart() {
    if (!file) return;
    setLocalError(null);
    setSignedBlob(null);
    setFileId(null);
    reset(); // clear the previous run's requestId so its cached Completed status can't re-trigger a stale download
    startedRef.current = false;
    fetchedRef.current = false;
    const fd = new FormData();
    fd.append('file', file);
    upload.mutate(fd, {
      onSuccess: (r) => setFileId(r.fileId),
      onError: (e) =>
        setLocalError(extractApiErrorMessage(e) ?? t('digitalSignature.padesOrchUploadFail', 'Tải lên thất bại')),
    });
  }

  const error = localError ?? padesError;
  const signTerminal = signStatus === 'Completed' || signStatus === 'Failed' || signStatus === 'Expired';
  const busy = upload.isPending || padesLoading
    || (Boolean(fileId) && scanStatus !== 'Available' && scanStatus !== 'Rejected')
    || (Boolean(requestId) && !signTerminal && !signedBlob);

  // Per-step status (wait/process/finish/error) derived from each phase's live sub-status — see buildPadesSteps.
  const stepItems = buildPadesSteps(
    {
      uploading: upload.isPending,
      fileId,
      scanStatus,
      padesLoading,
      requestId,
      signStatus,
      signedReady: Boolean(signedBlob),
      hasError: Boolean(error),
    },
    t,
  );

  return (
    <Card
      size="small"
      title={
        <Space>
          <FilePdfOutlined />
          {t('digitalSignature.padesOrchTitle', 'Ký PDF nhúng qua orchestrator (PAdES remote) [DEV]')}
        </Space>
      }
    >
      <Space direction="vertical" style={{ width: '100%' }}>
        <Paragraph type="secondary" style={{ marginBottom: 0 }}>
          {t(
            'digitalSignature.padesOrchDesc',
            'Tải PDF → quét virus → tạo yêu cầu ký remote (Mysign) → xác nhận OTP trên điện thoại → server ghép ' +
              'chữ ký. Khi hoàn tất, bấm Xem/Tải xuống để lấy PDF đã ký. Chọn luồng ký bên dưới để ghi đè cấu hình tenant cho lần này.',
          )}
        </Paragraph>

        <Space size="small" wrap>
          <Typography.Text type="secondary">{t('digitalSignature.padesOrchFlowLabel', 'Luồng ký:')}</Typography.Text>
          <Segmented<FlowChoice>
            size="small"
            value={flowChoice}
            disabled={busy}
            onChange={setFlowChoice}
            options={[
              { label: t('digitalSignature.padesOrchFlowTenant', 'Theo tenant'), value: 'tenant' },
              { label: t('digitalSignature.padesOrchFlowAsync', 'Async (nền)'), value: 'Async' },
              { label: t('digitalSignature.padesOrchFlowSync', 'Sync (chờ OTP)'), value: 'Sync' },
            ]}
          />
        </Space>

        <Upload
          accept=".pdf,application/pdf"
          maxCount={1}
          beforeUpload={(f: RcFile) => {
            setFile(f);
            setLocalError(null);
            setSignedBlob(null);
            return false; // hold the File; we POST it via useUploadFile ourselves
          }}
          onRemove={() => {
            setFile(null);
            return true;
          }}
        >
          <Button icon={<UploadOutlined />}>{t('digitalSignature.padesOrchChoose', 'Chọn PDF')}</Button>
        </Upload>

        <Button type="primary" icon={<FilePdfOutlined />} loading={busy} disabled={!file} onClick={handleStart}>
          {t('digitalSignature.padesOrchStart', 'Bắt đầu ký remote (OTP điện thoại)')}
        </Button>

        {(fileId || requestId || padesLoading) && (
          <Steps size="small" items={stepItems} />
        )}

        {error && (
          <Alert
            type="error"
            showIcon
            message={t('digitalSignature.padesOrchFailed', 'Ký PDF thất bại')}
            description={error}
          />
        )}

        {signedBlob && (
          <Alert
            type="success"
            showIcon
            message={t('digitalSignature.padesOrchOk', 'Đã ký PDF thành công (PAdES remote)')}
            description={
              <Space>
                <Button size="small" icon={<DownloadOutlined />} onClick={() => downloadBlob(signedBlob, 'pades-signed.pdf')}>
                  {t('common.download', 'Tải xuống')}
                </Button>
                <Button size="small" icon={<EyeOutlined />} onClick={() => setViewerOpen(true)}>
                  {t('common.view', 'Xem')}
                </Button>
              </Space>
            }
          />
        )}
      </Space>

      <SignedPdfViewer open={viewerOpen} blob={signedBlob} title="pades-signed.pdf" onClose={() => setViewerOpen(false)} />
    </Card>
  );
}
