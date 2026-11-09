/**
 * docflow-usb-card.tsx — DEV-only card on the signature demo page.
 *
 * Local-agent (USB token / VGCA DocFlow) sibling of pades-orchestrator-card.tsx: it drives the FULL PRODUCTION
 * USB-DocFlow path end-to-end — upload a PDF to Files → wait for the virus scan → create a `/signing/request` on
 * the DocFlow strategy → the VGCASignService desktop plugin prompts for the token PIN and signs → the signed PDF
 * lands back in Files → fetch it via the signing-steps endpoint for view/download.
 *
 * Unlike the remote card (phone OTP, server-side assembly), the USB ceremony requires the local agent to be
 * present and the metadata DocFlow stamps (signType / docNumber / issuedDate) to be supplied up front — so this
 * card gates the action on an agent probe and a small metadata form. Every API call is the real production
 * endpoint; only the card UI is dev-gated as the E2E vehicle.
 */

import { useState, useEffect, useRef, useCallback } from 'react';
import { Alert, Button, Card, Space, Steps, Typography, Upload } from 'antd';
import { UploadOutlined, UsbOutlined, EyeOutlined, DownloadOutlined } from '@ant-design/icons';
import type { RcFile } from 'antd/es/upload';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { extractApiErrorMessage } from '@/core/api/extract-api-error';
import { useUploadFile, useFileScanStatus } from '@/features/files/file-api';
import { useSignUploadedFile } from '@/features/files/use-sign-uploaded-file';
import { useSigningProviderInfo, useSigningRequestStatus } from './signature-api';
import { resolveAdapter, hasAdapter } from './registry';
import type { AgentProbeStatus } from './registry';
import { buildPadesSteps } from './pades-orchestrator-steps';
import { AgentStatusAlerts } from '@/features/files/sign-uploaded-file-agent-alerts';
import { DocFlowUsbMetaForm } from './docflow-usb-meta-form';
import type { DocFlowSignTypeName } from './signature-types';
import { fetchSigningSteps, fetchSignedStepBlob } from './signed-document/signed-document-api';
import { SignedPdfViewer } from './signed-document/signed-pdf-viewer';

// Side-effect: register adapters so the agent probe can resolve the USB-VGCA adapter.
import './providers/vgca-usb-adapter';
import './providers/remote-signing-adapter';

const { Paragraph } = Typography;

function stripExtension(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(0, dot) : name;
}

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

export function DocFlowUsbCard() {
  const { t } = useTranslation();
  const { data: providerInfo } = useSigningProviderInfo();
  const providerKey = providerInfo?.isEnabled ? providerInfo.providerKey : null;

  const [file, setFile] = useState<File | null>(null);
  const [fileId, setFileId] = useState<string | null>(null);
  const [signedBlob, setSignedBlob] = useState<Blob | null>(null);
  const [viewerOpen, setViewerOpen] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [signType, setSignType] = useState<DocFlowSignTypeName>('Issued');
  const [docNumber, setDocNumber] = useState('');
  const [issuedDate, setIssuedDate] = useState<Dayjs>(dayjs());
  const [agentStatus, setAgentStatus] = useState<AgentProbeStatus | 'checking' | 'not-required'>('checking');
  const [retryNonce, setRetryNonce] = useState(0);

  // Read latest form values inside the scan-effect without re-arming it on every keystroke.
  const metaRef = useRef({ signType, docNumber, issuedDate, fileName: '' });
  metaRef.current = { signType, docNumber, issuedDate, fileName: file?.name ?? '' };
  const startedRef = useRef(false); // fire startSigning once per scan-Available
  const fetchedRef = useRef(false); // fetch the signed PDF once per Completed

  const upload = useUploadFile();
  const scan = useFileScanStatus(fileId);
  const { startSigning, requestId, error: signError, isLoading: signLoading, reset } = useSignUploadedFile();
  const reqStatus = useSigningRequestStatus(requestId);

  const scanStatus = scan.data?.status;
  const signStatus = reqStatus.data?.status;

  // ── Agent probe (USB local agent) ──────────────────────────────────────────
  useEffect(() => {
    if (!providerKey) return;
    setAgentStatus('checking');
    let cancelled = false;
    (async () => {
      try {
        const adapter = hasAdapter(providerKey) ? resolveAdapter(providerKey) : null;
        if (adapter?.probeAgentStatus) {
          const status = await adapter.probeAgentStatus();
          if (!cancelled) setAgentStatus(status);
        } else if (!cancelled) {
          setAgentStatus('not-required');
        }
      } catch {
        if (!cancelled) setAgentStatus('unreachable');
      }
    })();
    return () => { cancelled = true; };
  }, [providerKey, retryNonce]);

  // ── Once the scan passes, run the USB ceremony (token PIN prompt fires here) ──
  useEffect(() => {
    if (fileId && scanStatus === 'Available' && providerKey && !startedRef.current) {
      startedRef.current = true;
      const m = metaRef.current;
      void startSigning({
        fileId,
        fileName: m.fileName,
        providerKey,
        signType: m.signType,
        docNumber: m.docNumber.trim() || stripExtension(m.fileName),
        issuedDate: m.issuedDate.toDate(),
      });
    }
    if (scanStatus === 'Rejected') {
      setLocalError(t('digitalSignature.docflowUsbRejected', 'File bị từ chối ở bước quét virus.'));
    }
  }, [fileId, scanStatus, providerKey, startSigning, t]);

  // ── On Completed, the signed PDF is back in Files — fetch it for view/download ──
  useEffect(() => {
    if (signStatus === 'Completed' && requestId && fileId && !fetchedRef.current) {
      fetchedRef.current = true;
      (async () => {
        const steps = await fetchSigningSteps('files', fileId);
        const step = steps.find((s) => s.signingRequestId === requestId);
        if (!step || !step.hasSignedPdf) {
          setLocalError(t('digitalSignature.docflowUsbNoSigned', 'Không tìm thấy PDF đã ký cho yêu cầu này.'));
          return;
        }
        setSignedBlob(await fetchSignedStepBlob('files', fileId, step));
      })().catch((e) =>
        setLocalError(extractApiErrorMessage(e) ?? t('digitalSignature.docflowUsbDownloadFail', 'Tải PDF đã ký thất bại')),
      );
    }
    if (signStatus === 'Failed' || signStatus === 'Expired') {
      setLocalError(t('digitalSignature.docflowUsbSignFail', 'Ký số thất bại (hết hạn hoặc lỗi).'));
    }
  }, [signStatus, requestId, fileId, t]);

  const handleStart = useCallback(() => {
    if (!file) return;
    reset(); // clear the previous run's requestId so its cached Completed status can't re-trigger a stale fetch
    setLocalError(null);
    setSignedBlob(null);
    setFileId(null);
    startedRef.current = false;
    fetchedRef.current = false;
    const fd = new FormData();
    fd.append('file', file);
    upload.mutate(fd, {
      onSuccess: (r) => setFileId(r.fileId),
      onError: (e) =>
        setLocalError(extractApiErrorMessage(e) ?? t('digitalSignature.docflowUsbUploadFail', 'Tải lên thất bại')),
    });
  }, [file, upload, reset, t]);

  const agentReady = agentStatus === 'available' || agentStatus === 'not-required';
  const signTerminal = signStatus === 'Completed' || signStatus === 'Failed' || signStatus === 'Expired';
  // A hung VGCA ceremony never reaches a terminal status; the poll caps out and surfaces timedOut. Treat it as a
  // settled (non-busy) state so the button frees up and the retry affordance below can drive a fresh poll — without
  // this, busy would stay true forever (the exact false-green hang this card exists to surface).
  const timedOut = reqStatus.timedOut;
  const busy =
    upload.isPending ||
    signLoading ||
    (Boolean(fileId) && scanStatus !== 'Available' && scanStatus !== 'Rejected') ||
    (Boolean(requestId) && !signTerminal && !signedBlob && !timedOut);

  // The hook's `error` is best-effort: the desktop tool signs/uploads over a channel the browser can't observe,
  // so a missing plugin callback does NOT mean failure — the durable status poll is the source of truth. Suppress
  // the hook error once the record reports Completed; genuine failures surface via signStatus + localError.
  const error = localError ?? (signStatus === 'Completed' ? null : signError);

  const stepItems = buildPadesSteps(
    {
      uploading: upload.isPending,
      fileId,
      scanStatus,
      padesLoading: signLoading,
      requestId,
      signStatus,
      signedReady: Boolean(signedBlob),
      hasError: Boolean(error),
    },
    t,
    {
      title: t('digitalSignature.docflowUsbStepSign', 'Ký bằng USB token'),
      process: t('digitalSignature.docflowUsbStepSignProc', 'Xác nhận trên USB Token (nhập PIN)…'),
      finish: t('digitalSignature.padesOrchOtpDone', 'Đã ký'),
      error: t('digitalSignature.docflowUsbStepSignErr', 'Ký thất bại'),
    },
  );

  return (
    <Card
      size="small"
      title={<Space><UsbOutlined />{t('digitalSignature.docflowUsbTitle', 'Ký PDF bằng USB Token (VGCA DocFlow) [DEV]')}</Space>}
    >
      <Space direction="vertical" style={{ width: '100%' }}>
        <Paragraph type="secondary" style={{ marginBottom: 0 }}>
          {t(
            'digitalSignature.docflowUsbDesc',
            'Cắm USB Token + mở VGCASignService → chọn PDF, nhập thông tin văn bản → quét virus → plugin yêu cầu PIN ' +
              'và ký → PDF đã ký lưu vào danh sách Tệp. Khi hoàn tất, bấm Xem/Tải xuống.',
          )}
        </Paragraph>

        {providerKey && <AgentStatusAlerts agentStatus={agentStatus} onRetry={() => setRetryNonce((n) => n + 1)} />}

        <DocFlowUsbMetaForm
          signType={signType}
          docNumber={docNumber}
          issuedDate={issuedDate}
          docNumberPlaceholder={file ? stripExtension(file.name) : ''}
          disabled={busy}
          onSignType={setSignType}
          onDocNumber={setDocNumber}
          onIssuedDate={setIssuedDate}
        />

        <Upload
          accept=".pdf,application/pdf"
          maxCount={1}
          beforeUpload={(f: RcFile) => {
            setFile(f);
            setLocalError(null);
            setSignedBlob(null);
            return false; // hold the File; we POST it via useUploadFile ourselves
          }}
          onRemove={() => { setFile(null); return true; }}
        >
          <Button icon={<UploadOutlined />}>{t('digitalSignature.docflowUsbChoose', 'Chọn PDF')}</Button>
        </Upload>

        <Button type="primary" icon={<UsbOutlined />} loading={busy} disabled={!file || !agentReady} onClick={handleStart}>
          {t('digitalSignature.docflowUsbStart', 'Ký bằng USB Token (nhập PIN)')}
        </Button>

        {(fileId || requestId || signLoading) && <Steps size="small" items={stepItems} />}

        {/* Poll capped out without a terminal status — the ceremony may still have completed (the desktop tool signs
            over a channel the browser can't observe). Offer a retry that re-arms the poll instead of leaving a spinner. */}
        {timedOut && !signedBlob && (
          <Alert
            type="warning"
            showIcon
            message={t('digitalSignature.pollTimedOut', 'Hết thời gian chờ')}
            description={
              <Space direction="vertical" size="small">
                <span>
                  {t('digitalSignature.docflowUsbTimedOutDesc',
                    'Chưa nhận được xác nhận hoàn tất. Việc ký có thể vẫn thành công — kiểm tra danh sách Tệp, hoặc thử lại.')}
                </span>
                <Button size="small" onClick={() => reqStatus.retryPoll()}>
                  {t('common.retry', 'Thử lại')}
                </Button>
              </Space>
            }
          />
        )}

        {error && !signedBlob && (
          <Alert type="error" showIcon message={t('digitalSignature.docflowUsbFailed', 'Ký PDF thất bại')} description={error} />
        )}

        {signedBlob && (
          <Alert
            type="success"
            showIcon
            message={t('digitalSignature.docflowUsbOk', 'Đã ký PDF thành công (USB DocFlow)')}
            description={
              <Space>
                <Button size="small" icon={<DownloadOutlined />} onClick={() => downloadBlob(signedBlob, 'usb-signed.pdf')}>
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

      <SignedPdfViewer open={viewerOpen} blob={signedBlob} title="usb-signed.pdf" onClose={() => setViewerOpen(false)} />
    </Card>
  );
}
