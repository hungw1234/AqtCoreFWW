/**
 * sign-submission-document-button.tsx — action button + modal to sign a form submission's
 * generated PDF via USB DocFlow ceremony.
 *
 * Gating:
 *   - PDF-eligible: submission status is 'Approved' (server-generated PDF exists/is stable).
 *   - Provider configured: tenant signing config is enabled (useSigningProviderInfo).
 *
 * Modal flow:
 *   1. Provider gate — if signing disabled, show unavailable alert (same keys as files modal).
 *   2. Agent probe — probe adapter status on open; route not-installed / unreachable alerts
 *      via AgentStatusAlerts (reused from sign-uploaded-file-agent-alerts.tsx).
 *   3. Metadata form — signType / docNumber / issuedDate.
 *   4. Cert-not-verified caveat — MANDATORY until the post-upload cert-verification sandbox
 *      ships. Shown during signing AND on success result per the security requirement.
 *   5. POST → VGCA ceremony → poll → Completed / timedOut / Failed.
 *
 * On Completed: submission list is invalidated (hook), view reflects "Đã ký".
 * If submission already has a signingRequestId, polls that on mount so a page reload
 * shows live status instead of the action button.
 */

import { useState, useEffect, useCallback } from 'react';
import {
  Modal, Alert, Button, Space, Form, Input, Select, Spin, Typography, Result, Tooltip, Drawer,
} from 'antd';
import { DatePicker } from 'antd';
import { SafetyCertificateOutlined, HistoryOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import dayjs, { type Dayjs } from 'dayjs';
import { useSigningProviderInfo, useSigningRequestStatus } from '@/features/digital-signature/signature-api';
import { resolveAdapter, hasAdapter } from '@/features/digital-signature/registry';
import type { AgentProbeStatus } from '@/features/digital-signature/registry';
import type { DocFlowSignTypeName } from '@/features/digital-signature/signature-types';
import { AgentStatusAlerts } from '@/features/files/sign-uploaded-file-agent-alerts';
import { SigningStepsPanel } from '@/features/digital-signature/signed-document/signing-steps-panel';
import { useSignSubmissionDocument } from './use-sign-submission-document';
import type { SubmissionStatus } from './form-types';

// Side-effect: ensure adapters are registered before probe
import '@/features/digital-signature/providers/vgca-usb-adapter';
import '@/features/digital-signature/providers/remote-signing-adapter';

const { Text } = Typography;

// ─── Sign type options (mirrors sign-uploaded-file-modal.tsx) ─────────────────

const SIGN_TYPE_OPTIONS: Array<{ value: DocFlowSignTypeName; labelKey: string; labelFallback: string }> = [
  { value: 'Issued',   labelKey: 'digitalSignature.signType.Issued',   labelFallback: 'Phát hành' },
  { value: 'Approved', labelKey: 'digitalSignature.signType.Approved', labelFallback: 'Phê duyệt' },
  { value: 'Income',   labelKey: 'digitalSignature.signType.Income',   labelFallback: 'Công văn đến' },
  { value: 'Comment',  labelKey: 'digitalSignature.signType.Comment',  labelFallback: 'Góp ý' },
  { value: 'Appendix', labelKey: 'digitalSignature.signType.Appendix', labelFallback: 'Phụ lục' },
  { value: 'Copy',     labelKey: 'digitalSignature.signType.Copy',     labelFallback: 'Bản sao' },
];

// ─── PDF-eligible gate ────────────────────────────────────────────────────────

/**
 * Returns true when a submission status qualifies for PDF signing.
 * Approved = server-generated PDF exists and is finalized.
 * Pending / Rejected submissions have no stable PDF to sign.
 */
export function isSubmissionPdfEligible(status: SubmissionStatus): boolean {
  return status === 'Approved';
}

// ─── Props ────────────────────────────────────────────────────────────────────

export interface SignSubmissionDocumentButtonProps {
  /** Public id (GUID) of the submission — used in the API call. */
  publicId: string;
  /** Template public id — used for query invalidation on Completed. */
  templateId: string;
  /** Current submission status — gates rendering. */
  status: SubmissionStatus;
  /**
   * Optional existing signing request id from the submission read model.
   * When present, the hook polls this on mount so a page reload shows live status.
   */
  existingRequestId?: string | null;
  /** Human-readable submission reference for prefilling docNumber (e.g. "Đơn xin nghỉ phép"). */
  submissionLabel?: string;
  /** Render as icon-only button (for table rows). Default: false (shows label text). */
  iconOnly?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export function SignSubmissionDocumentButton({
  publicId,
  templateId,
  status,
  existingRequestId,
  submissionLabel,
  iconOnly = false,
}: SignSubmissionDocumentButtonProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);

  const { data: signingProvider, isLoading: providerLoading } = useSigningProviderInfo(open);
  const providerKey = signingProvider?.isEnabled ? signingProvider.providerKey : null;

  const [agentStatus, setAgentStatus] = useState<AgentProbeStatus | 'checking' | 'not-required'>('checking');
  const [retryNonce, setRetryNonce] = useState(0);
  const [docNumber, setDocNumber] = useState(submissionLabel ?? `Đơn ${publicId.slice(0, 8)}`);
  const [issuedDate, setIssuedDate] = useState<Dayjs>(dayjs());
  const [signType, setSignType] = useState<DocFlowSignTypeName>('Approved');

  const { isLoading, error, requestId, startSigning, clearError } = useSignSubmissionDocument();

  // Poll the existing request on mount (page-reload resume) OR the new one after start.
  const activeRequestId = requestId ?? existingRequestId ?? undefined;
  const statusQuery = useSigningRequestStatus(activeRequestId as string | undefined);
  const signingStatus = statusQuery.data?.status;

  // ── Derived state ────────────────────────────────────────────────────────────

  const isCompleted = signingStatus === 'Completed';
  const isFailed = signingStatus === 'Failed' || signingStatus === 'Expired';
  const ceremonyDone = isCompleted || isFailed || statusQuery.timedOut;
  const agentReady = agentStatus === 'available' || agentStatus === 'not-required';
  const canSign = !!providerKey && !isLoading && !requestId && !existingRequestId && agentReady;

  // ── Reset when modal opens ────────────────────────────────────────────────────
  // All hooks must be declared before any early return (Rules of Hooks).

  const handleOpen = useCallback(() => {
    setDocNumber(submissionLabel ?? `Đơn ${publicId.slice(0, 8)}`);
    setIssuedDate(dayjs());
    setSignType('Approved');
    setAgentStatus('checking');
    setRetryNonce(0);
    setOpen(true);
  }, [submissionLabel, publicId]);

  // ── Agent probe ───────────────────────────────────────────────────────────────

  useEffect(() => {
    if (!open || !providerKey) return;
    setAgentStatus('checking');
    let cancelled = false;
    (async () => {
      try {
        const adapter = hasAdapter(providerKey) ? resolveAdapter(providerKey) : null;
        if (adapter?.probeAgentStatus) {
          const s = await adapter.probeAgentStatus();
          if (!cancelled) setAgentStatus(s);
        } else {
          if (!cancelled) setAgentStatus('not-required');
        }
      } catch {
        if (!cancelled) setAgentStatus('unreachable');
      }
    })();
    return () => { cancelled = true; };
  }, [open, providerKey, retryNonce]);

  // ── Sign handler ──────────────────────────────────────────────────────────────

  const handleSign = useCallback(async () => {
    if (!providerKey) return;
    clearError();
    await startSigning({
      publicId,
      templateId,
      providerKey,
      signType,
      docNumber: docNumber.trim() || `submission-${publicId}`,
      issuedDate: issuedDate.toDate(),
      fileName: `submission-${publicId}.pdf`,
    });
  }, [providerKey, publicId, templateId, signType, docNumber, issuedDate, startSigning, clearError]);

  // ── Gate: not PDF-eligible (after all hooks) ──────────────────────────────────

  if (!isSubmissionPdfEligible(status)) return null;

  // ── Trigger button ────────────────────────────────────────────────────────────

  // Already signed (existing request completed) — show "Đã ký" badge + "Lịch sử ký" action.
  const alreadySigned = isCompleted && existingRequestId && !requestId;
  if (alreadySigned) {
    return (
      <>
        <Space size={4}>
          <Tooltip title={t('forms.submission.signedTooltip', 'Tài liệu đã được ký số')}>
            <Button size="small" icon={<SafetyCertificateOutlined />} disabled>
              {!iconOnly && t('forms.submission.signed', 'Đã ký')}
            </Button>
          </Tooltip>
          <Tooltip title={t('digitalSignature.signedDocument.historyTitle', 'Lịch sử ký')}>
            <Button
              size="small"
              icon={<HistoryOutlined />}
              onClick={() => setHistoryOpen(true)}
            />
          </Tooltip>
        </Space>

        {/* Signing history drawer — reuses the shared SigningStepsPanel; no Forms-specific viewer */}
        <Drawer
          title={t('digitalSignature.signedDocument.historyTitle', 'Lịch sử ký')}
          open={historyOpen}
          onClose={() => setHistoryOpen(false)}
          width={640}
          destroyOnHidden
        >
          <SigningStepsPanel scope="forms" id={publicId} />
        </Drawer>
      </>
    );
  }

  return (
    <>
      <Tooltip title={t('forms.submission.signDocument', 'Ký số tài liệu')}>
        <Button
          size="small"
          icon={<SafetyCertificateOutlined />}
          onClick={handleOpen}
        >
          {!iconOnly && t('forms.submission.signDocument', 'Ký số tài liệu')}
        </Button>
      </Tooltip>

      {/* ── Modal ──────────────────────────────────────────────────────────── */}

      <Modal
        open={open}
        title={
          <Space>
            <SafetyCertificateOutlined />
            {t('forms.submission.signDocumentTitle', 'Ký số tài liệu phiếu')}
          </Space>
        }
        onCancel={() => setOpen(false)}
        onOk={handleSign}
        okText={t('digitalSignature.sign', 'Ký số')}
        cancelText={t('common.cancel', 'Huỷ')}
        okButtonProps={{ disabled: !canSign, loading: isLoading }}
        // Only override footer once the ceremony is done (show a Close-only footer).
        // When ceremonyDone=false the prop is omitted so antd renders its default OK+Cancel footer.
        // Passing footer={undefined} explicitly suppresses the footer in antd v5.
        {...(ceremonyDone ? {
          footer: (
            <Button type="primary" onClick={() => setOpen(false)}>
              {t('common.close', 'Đóng')}
            </Button>
          ),
        } : {})}
        destroyOnHidden
        width={520}
      >
        <Space direction="vertical" style={{ width: '100%' }} size="middle">

          {providerLoading && (
            <Space>
              <Spin size="small" />
              <Text type="secondary">{t('common.loading', 'Đang tải...')}</Text>
            </Space>
          )}

          {/* Provider unavailable */}
          {!providerLoading && signingProvider && !providerKey && (
            <Alert
              type="warning"
              showIcon
              message={t('digitalSignature.unavailable', 'Ký số chưa khả dụng')}
              description={t(
                'digitalSignature.unavailableContactAdmin',
                'Tổ chức chưa cấu hình nhà cung cấp chữ ký số. Vui lòng liên hệ quản trị viên để bật ký số.',
              )}
            />
          )}

          {/* Agent probe state — checking / not-installed / unreachable */}
          {!providerLoading && providerKey && (
            <AgentStatusAlerts
              agentStatus={agentStatus}
              onRetry={() => setRetryNonce((n) => n + 1)}
            />
          )}

          {/* MANDATORY cert-not-verified caveat — visible until the post-upload cert-verification
              sandbox ships. Form-gen USB-signed PDFs are stored as signed WITHOUT cert==signer check. */}
          {providerKey && agentReady && !ceremonyDone && (
            <Alert
              type="info"
              showIcon
              message={t(
                'digitalSignature.uploadedNotVerifiedCaveat',
                'Chữ ký số chưa được xác minh người ký. Tài liệu này sẽ được lưu là đã ký số, nhưng việc xác minh chứng thư số của người ký sẽ được thực hiện ở bước sau.',
              )}
            />
          )}

          {/* Metadata form — only when agent ready and ceremony not started */}
          {providerKey && agentReady && !requestId && !existingRequestId && (
            <Form layout="vertical" size="small">
              <Form.Item label={t('digitalSignature.signType', 'Loại ký')}>
                <Select
                  value={signType}
                  onChange={(v) => setSignType(v as DocFlowSignTypeName)}
                  options={SIGN_TYPE_OPTIONS.map((o) => ({
                    value: o.value,
                    label: t(o.labelKey, o.labelFallback),
                  }))}
                />
              </Form.Item>
              <Form.Item label={t('digitalSignature.docNumber', 'Số hiệu văn bản')}>
                <Input
                  value={docNumber}
                  onChange={(e) => setDocNumber(e.target.value)}
                  maxLength={100}
                />
              </Form.Item>
              <Form.Item label={t('digitalSignature.issuedDate', 'Ngày ban hành')}>
                <DatePicker
                  value={issuedDate}
                  onChange={(v) => setIssuedDate(v ?? dayjs())}
                  format="DD/MM/YYYY"
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Form>
          )}

          {isLoading && (
            <Space>
              <Spin size="small" />
              <Text type="secondary">
                {t('digitalSignature.signingInProgress', 'Đang thực hiện ký số...')}
              </Text>
            </Space>
          )}

          {activeRequestId && !isLoading && !ceremonyDone && (
            <Space>
              <Spin size="small" />
              <Text type="secondary">
                {t('digitalSignature.waitingCompletion', 'Đang chờ xác nhận hoàn tất...')}
              </Text>
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
                  <span>
                    {t(
                      'digitalSignature.pollTimedOutDesc',
                      'Không nhận được xác nhận hoàn tất từ máy chủ. Vui lòng kiểm tra lại danh sách tệp.',
                    )}
                  </span>
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
                  <span>
                    {t(
                      'forms.submission.signedSuccessDesc',
                      'Tài liệu phiếu đã được ký số. Phiếu sẽ được cập nhật trạng thái đã ký.',
                    )}
                  </span>
                  {/* Restate the trust caveat at success — signature is stored but signer's
                      certificate has NOT yet been verified (deferred to the sandbox step). */}
                  <Text type="secondary">
                    {t(
                      'digitalSignature.uploadedNotVerifiedCaveat',
                      'Chữ ký số chưa được xác minh người ký. Tài liệu này sẽ được lưu là đã ký số, nhưng việc xác minh chứng thư số của người ký sẽ được thực hiện ở bước sau.',
                    )}
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
              description={
                statusQuery.data?.failureReason ??
                t('digitalSignature.unknownFailure', 'Không xác định được nguyên nhân lỗi.')
              }
            />
          )}

          {error && !isFailed && (
            <Alert
              type="error"
              showIcon
              message={t('digitalSignature.failed', 'Ký số thất bại')}
              description={error}
            />
          )}

        </Space>
      </Modal>
    </>
  );
}
