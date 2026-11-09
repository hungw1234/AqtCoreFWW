/**
 * sign-uploaded-file-modal.tsx — modal to sign an uploaded PDF via USB DocFlow.
 *
 * Provider: pinned to USB_PROVIDER_KEY ('usb-vgca'). USB DocFlow is a client-side VGCA ceremony
 * that does not depend on the tenant's active-provider config — the BE resolves the provider from
 * this key alone. Eligibility (does the user hold an approved UsbTokenVgca enrollment?) is gated by
 * the caller (file-list-page), which only opens this modal for an eligible user.
 *
 * Agent probe: calls probeAgentStatus() on the resolved adapter before the ceremony.
 * Delegates agent status rendering to AgentStatusAlerts (sign-uploaded-file-agent-alerts.tsx).
 *
 * Caveat: a persistent "cert not yet verified" alert is shown because the cert==signer
 * binding check is deferred to a later phase (PDF sandbox parse). Until then, uploaded
 * PDFs are stored as signed WITHOUT full cert verification.
 *
 * Poll: after the VGCA ceremony resolves, useSigningRequestStatus polls until
 * Completed/Failed/Expired/timedOut. On Completed the files list is invalidated.
 */

import { useState, useEffect, useCallback } from 'react';
import { Modal, Alert, Button, Space, Form, Input, Select, Spin, Typography, Result } from 'antd';
import { DatePicker } from 'antd';
import { SafetyCertificateOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import dayjs, { type Dayjs } from 'dayjs';
import { useSigningRequestStatus } from '@/features/digital-signature/signature-api';
import { resolveAdapter, hasAdapter } from '@/features/digital-signature/registry';
import type { AgentProbeStatus } from '@/features/digital-signature/registry';
import type { DocFlowSignTypeName } from '@/features/digital-signature/signature-types';
import { AgentStatusAlerts } from './sign-uploaded-file-agent-alerts';
import { useSignUploadedFile } from './use-sign-uploaded-file';

// Side-effect: ensure adapters are registered before probe
import '@/features/digital-signature/providers/vgca-usb-adapter';
import '@/features/digital-signature/providers/remote-signing-adapter';

const { Text } = Typography;

// USB DocFlow provider key — pinned (mirrors REMOTE_MYSIGN_PROVIDER_KEY on the PAdES hook and
// USB_PROVIDER_KEY in enroll-cert-modal). USB signing is client-side and tenant-config-independent:
// the BE resolves the provider from this key alone, so sending the tenant default (which may be a
// remote provider) would fail to route to the DocFlow strategy. The caller only opens this modal
// when the user has an approved UsbTokenVgca enrollment.
const USB_PROVIDER_KEY = 'usb-vgca';

// ─── Props ────────────────────────────────────────────────────────────────────

export interface SignUploadedFileModalProps {
  open: boolean;
  fileId: string;
  /** Original file name — prefills DocNumber (sans extension) and VGCA FileName. */
  originalFileName: string;
  onClose: () => void;
}

// ─── Sign type options ────────────────────────────────────────────────────────

const SIGN_TYPE_OPTIONS: Array<{ value: DocFlowSignTypeName; labelKey: string; labelFallback: string }> = [
  { value: 'Issued',   labelKey: 'digitalSignature.signType.Issued',   labelFallback: 'Phát hành' },
  { value: 'Approved', labelKey: 'digitalSignature.signType.Approved', labelFallback: 'Phê duyệt' },
  { value: 'Income',   labelKey: 'digitalSignature.signType.Income',   labelFallback: 'Công văn đến' },
  { value: 'Comment',  labelKey: 'digitalSignature.signType.Comment',  labelFallback: 'Góp ý' },
  { value: 'Appendix', labelKey: 'digitalSignature.signType.Appendix', labelFallback: 'Phụ lục' },
  { value: 'Copy',     labelKey: 'digitalSignature.signType.Copy',     labelFallback: 'Bản sao' },
];

// Strip file extension for use as a default document reference number
function stripExtension(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot > 0 ? name.slice(0, dot) : name;
}

// ─── Component ────────────────────────────────────────────────────────────────

export function SignUploadedFileModal({
  open,
  fileId,
  originalFileName,
  onClose,
}: SignUploadedFileModalProps) {
  const { t } = useTranslation();

  const providerKey = USB_PROVIDER_KEY;

  const [agentStatus, setAgentStatus] = useState<AgentProbeStatus | 'checking' | 'not-required'>('checking');
  const [retryNonce, setRetryNonce] = useState(0);
  const [docNumber, setDocNumber] = useState(stripExtension(originalFileName));
  const [issuedDate, setIssuedDate] = useState<Dayjs>(dayjs());
  const [signType, setSignType] = useState<DocFlowSignTypeName>('Issued');

  const { isLoading, error, requestId, startSigning, clearError } = useSignUploadedFile();
  const statusQuery = useSigningRequestStatus(requestId);
  const signingStatus = statusQuery.data?.status;

  // ── Reset when modal opens ────────────────────────────────────────────────

  useEffect(() => {
    if (open) {
      setDocNumber(stripExtension(originalFileName));
      setIssuedDate(dayjs());
      setSignType('Issued');
      setAgentStatus('checking');
      setRetryNonce(0);
    }
  }, [open, originalFileName]);

  // ── Agent probe ───────────────────────────────────────────────────────────

  useEffect(() => {
    if (!open || !providerKey) return;
    setAgentStatus('checking');
    let cancelled = false;
    (async () => {
      try {
        const adapter = hasAdapter(providerKey) ? resolveAdapter(providerKey) : null;
        if (adapter?.probeAgentStatus) {
          const status = await adapter.probeAgentStatus();
          if (!cancelled) setAgentStatus(status);
        } else {
          if (!cancelled) setAgentStatus('not-required');
        }
      } catch {
        if (!cancelled) setAgentStatus('unreachable');
      }
    })();
    return () => { cancelled = true; };
  }, [open, providerKey, retryNonce]);

  // ── Sign handler ──────────────────────────────────────────────────────────

  const handleSign = useCallback(async () => {
    if (!providerKey) return;
    clearError();
    await startSigning({
      fileId,
      fileName: originalFileName,
      providerKey,
      signType,
      docNumber: docNumber.trim() || stripExtension(originalFileName),
      issuedDate: issuedDate.toDate(),
    });
  }, [providerKey, fileId, originalFileName, signType, docNumber, issuedDate, startSigning, clearError]);

  // ── Derived state ─────────────────────────────────────────────────────────

  const isCompleted = signingStatus === 'Completed';
  const isFailed = signingStatus === 'Failed' || signingStatus === 'Expired';
  const ceremonyDone = isCompleted || isFailed || statusQuery.timedOut;
  const agentReady = agentStatus === 'available' || agentStatus === 'not-required';

  const canSign =
    !!providerKey && !isLoading && !requestId && agentReady;

  return (
    <Modal
      open={open}
      title={<Space><SafetyCertificateOutlined />{t('digitalSignature.sign', 'Ký số')}</Space>}
      onCancel={onClose}
      onOk={handleSign}
      okText={t('digitalSignature.sign', 'Ký số')}
      cancelText={t('common.cancel', 'Huỷ')}
      okButtonProps={{ disabled: !canSign, loading: isLoading }}
      footer={ceremonyDone ? (
        <Button type="primary" onClick={onClose}>{t('common.close', 'Đóng')}</Button>
      ) : undefined}
      destroyOnHidden
      width={520}
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">

        {/* Agent probe state — checking / not-installed / unreachable */}
        {providerKey && (
          <AgentStatusAlerts
            agentStatus={agentStatus}
            onRetry={() => setRetryNonce(n => n + 1)}
          />
        )}

        {/* Cert-not-verified caveat — visible until Phase 2c-D cert sandbox ships.
            Uploaded PDFs are stored as signed WITHOUT cert==signer verification. */}
        {providerKey && agentReady && !ceremonyDone && (
          <Alert
            type="info"
            showIcon
            message={t('digitalSignature.uploadedNotVerifiedCaveat',
              'Chữ ký số chưa được xác minh người ký. Tài liệu này sẽ được lưu là đã ký số, nhưng việc xác minh chứng thư số của người ký sẽ được thực hiện ở bước sau.')}
          />
        )}

        {/* Metadata form — only when agent ready and ceremony not started */}
        {providerKey && agentReady && !requestId && (
          <Form layout="vertical" size="small">
            <Form.Item label={t('digitalSignature.signType', 'Loại ký')}>
              <Select
                value={signType}
                onChange={(v) => setSignType(v as DocFlowSignTypeName)}
                options={SIGN_TYPE_OPTIONS.map(o => ({
                  value: o.value,
                  label: t(o.labelKey, o.labelFallback),
                }))}
              />
            </Form.Item>
            <Form.Item label={t('digitalSignature.docNumber', 'Số hiệu văn bản')}>
              <Input
                value={docNumber}
                onChange={e => setDocNumber(e.target.value)}
                maxLength={100}
                placeholder={stripExtension(originalFileName)}
              />
            </Form.Item>
            <Form.Item label={t('digitalSignature.issuedDate', 'Ngày ban hành')}>
              <DatePicker
                value={issuedDate}
                onChange={v => setIssuedDate(v ?? dayjs())}
                format="DD/MM/YYYY"
                style={{ width: '100%' }}
              />
            </Form.Item>
          </Form>
        )}

        {isLoading && (
          <Space>
            <Spin size="small" />
            <Text type="secondary">{t('digitalSignature.signingInProgress', 'Đang thực hiện ký số...')}</Text>
          </Space>
        )}

        {requestId && !isLoading && !ceremonyDone && (
          <Space>
            <Spin size="small" />
            <Text type="secondary">{t('digitalSignature.waitingCompletion', 'Đang chờ xác nhận hoàn tất...')}</Text>
          </Space>
        )}

        {/* Poll timed out */}
        {statusQuery.timedOut && (
          <Alert
            type="warning"
            showIcon
            message={t('digitalSignature.pollTimedOut', 'Hết thời gian chờ')}
            description={
              <Space direction="vertical" size="small">
                <span>{t('digitalSignature.pollTimedOutDesc', 'Không nhận được xác nhận hoàn tất từ máy chủ. Vui lòng kiểm tra lại danh sách tệp.')}</span>
                <Button size="small" onClick={() => statusQuery.retryPoll()}>
                  {t('common.retry', 'Thử lại')}
                </Button>
              </Space>
            }
          />
        )}

        {isCompleted && (
          <Result
            status="success"
            title={t('digitalSignature.success', 'Ký số thành công')}
            subTitle={
              <Space direction="vertical" size="small">
                <span>{t('digitalSignature.uploadedSignedDesc', 'Tệp PDF đã được ký số. Danh sách tệp sẽ được cập nhật.')}</span>
                {/* Restate the trust caveat at the moment it matters most — the signature is stored
                    but the signer's certificate has not yet been verified (deferred to the sandbox step). */}
                <Text type="secondary">
                  {t('digitalSignature.uploadedNotVerifiedCaveat',
                    'Chữ ký số chưa được xác minh người ký. Tài liệu này sẽ được lưu là đã ký số, nhưng việc xác minh chứng thư số của người ký sẽ được thực hiện ở bước sau.')}
                </Text>
              </Space>
            }
          />
        )}

        {isFailed && (
          <Alert
            type="error"
            showIcon
            message={t('digitalSignature.failed', 'Ký số thất bại')}
            description={statusQuery.data?.failureReason ?? t('digitalSignature.unknownFailure', 'Không xác định được nguyên nhân lỗi.')}
          />
        )}

        {/* The hook's `error` (e.g. the VGCA plugin callback timing out) is best-effort: the desktop
            tool signs and uploads over a channel the browser can't observe, so a missing callback does
            NOT mean the signing failed. The durable signing-request status poll is the source of truth —
            suppress this error once the record reports Completed so we never show a failure alert on top
            of the success result. Genuine terminal failures surface via `isFailed` above. */}
        {error && !isFailed && !isCompleted && (
          <Alert
            type="error"
            showIcon
            message={t('digitalSignature.failed', 'Ký số thất bại')}
            description={error}
          />
        )}

      </Space>
    </Modal>
  );
}
