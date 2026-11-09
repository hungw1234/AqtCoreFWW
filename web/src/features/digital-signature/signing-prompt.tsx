/**
 * signing-prompt.tsx — capability-aware reusable signing component.
 *
 * Probes the active tenant provider, detects RequiresLocalAgent capability,
 * checks agent availability, and routes to the correct adapter for signing.
 * Renders install-guide link when VGCA agent is missing.
 */

import { useState, useEffect, useCallback } from 'react';
import { Alert, Button, Input, Modal, Spin, Space, Tag, Typography } from 'antd';
import { SafetyCertificateOutlined, WarningOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { resolveAdapter, hasAdapter } from './registry';
import { hasCapability } from './signature-types';
import type { SignResult } from './signature-types';

// Side-effect: import adapters so they self-register before first use
import './providers/vgca-usb-adapter';
import './providers/remote-signing-adapter';

const { Text } = Typography;

// ─── Props ────────────────────────────────────────────────────────────────────

export interface SigningPromptProps {
  /** Whether the modal is open */
  open: boolean;
  /** Provider key from tenant config (e.g. 'usb-vgca', 'remote-vnpt-smartca') */
  providerKey: string | null;
  /**
   * Capability string from the active provider's AvailableProviderDto.capabilities.
   * Comma-separated BE enum names, e.g. "ServerSideSigning, RequiresLocalAgent".
   * Pass empty string when capabilities are unknown — agent probe will be skipped.
   */
  capabilities: string;
  /** Payload to sign (raw bytes) */
  payload: Uint8Array | null;
  /** Optional signing reason / document description shown to user */
  reason?: string;
  /** Called with the full sign result on success — carries signerCertBase64 + format
   *  metadata the verify call needs (dropping these mis-routes Raw sigs to the CMS parser). */
  onSuccess: (result: SignResult) => void;
  /** Called when user cancels or modal closes */
  onCancel: () => void;
}

// ─── Agent status type ────────────────────────────────────────────────────────

// 'missing' = plugin never installed (install guide); 'unreachable' = plugin present but
// not responding / token not ready (retry). Splitting these stops a transient probe
// failure from showing the misleading "install the software" message.
type AgentStatus = 'checking' | 'available' | 'missing' | 'unreachable' | 'not-required';

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * SigningPrompt — modal that orchestrates digital signing.
 * Handles: local-agent detection, reason input, sign action, pending state.
 */
export function SigningPrompt({
  open,
  providerKey,
  capabilities,
  payload,
  reason: defaultReason,
  onSuccess,
  onCancel,
}: SigningPromptProps) {
  const { t } = useTranslation();

  const [agentStatus, setAgentStatus] = useState<AgentStatus>('checking');
  const [reason, setReason] = useState(defaultReason ?? '');
  const [isSigning, setIsSigning] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [isPending, setIsPending] = useState(false);
  // Bumped by the "retry" affordance to re-run the agent probe (effect dep).
  const [retryNonce, setRetryNonce] = useState(0);

  // Derive from comma-separated capability string (JsonStringEnumConverter + [Flags])
  const requiresLocalAgent = hasCapability(capabilities, 'RequiresLocalAgent');

  // ── Agent probe on open ───────────────────────────────────────────────────

  useEffect(() => {
    if (!open || !providerKey) return;

    setErrorMsg(null);
    setIsPending(false);

    if (!requiresLocalAgent) {
      setAgentStatus('not-required');
      return;
    }

    setAgentStatus('checking');

    let cancelled = false;
    (async () => {
      try {
        const adapter = hasAdapter(providerKey) ? resolveAdapter(providerKey) : null;
        if (adapter?.probeAgentStatus) {
          // probeAgentStatus classifies by cause and never throws; map to UI state.
          const status = await adapter.probeAgentStatus();
          if (!cancelled) {
            setAgentStatus(
              status === 'available' ? 'available' : status === 'not-installed' ? 'missing' : 'unreachable',
            );
          }
        } else {
          if (!cancelled) setAgentStatus('not-required');
        }
      } catch {
        // Defensive: a misbehaving adapter that throws is treated as unreachable, not
        // not-installed — we already know the provider is configured at this point.
        if (!cancelled) setAgentStatus('unreachable');
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [open, providerKey, requiresLocalAgent, retryNonce]);

  // Reset reason when default changes
  useEffect(() => {
    setReason(defaultReason ?? '');
  }, [defaultReason]);

  // ── Sign handler ──────────────────────────────────────────────────────────

  const handleSign = useCallback(async () => {
    if (!providerKey || !payload) return;

    setErrorMsg(null);
    setIsSigning(true);
    try {
      const adapter = resolveAdapter(providerKey);
      const result = await adapter.sign(payload, { reason: reason || undefined });

      if (result.isPending) {
        setIsPending(true);
        setIsSigning(false);
        // Pending = async approval (SmartCA mobile) — user must wait for completion
        return;
      }

      onSuccess(result);
    } catch (err) {
      setErrorMsg(err instanceof Error ? err.message : t('digitalSignature.failed', 'Ký số thất bại'));
    } finally {
      setIsSigning(false);
    }
  }, [providerKey, payload, reason, onSuccess, t]);

  // ── Derived state ─────────────────────────────────────────────────────────

  const canSign =
    !!providerKey &&
    !!payload &&
    !isSigning &&
    !isPending &&
    agentStatus !== 'missing' &&
    agentStatus !== 'unreachable' &&
    agentStatus !== 'checking';

  const title = (
    <Space>
      <SafetyCertificateOutlined />
      {t('digitalSignature.title', 'Ký số điện tử')}
      {providerKey && <Tag color="blue">{providerKey}</Tag>}
    </Space>
  );

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <Modal
      title={title}
      open={open}
      onCancel={onCancel}
      onOk={handleSign}
      okText={t('digitalSignature.sign', 'Ký số')}
      cancelText={t('common.cancel', 'Huỷ')}
      okButtonProps={{ disabled: !canSign, loading: isSigning }}
      destroyOnHidden
      width={480}
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">

        {/* Agent checking spinner */}
        {agentStatus === 'checking' && (
          <Space>
            <Spin size="small" />
            <Text type="secondary">
              {t('digitalSignature.agentChecking', 'Đang kiểm tra agent cục bộ...')}
            </Text>
          </Space>
        )}

        {/* Agent missing — install guide */}
        {agentStatus === 'missing' && (
          <Alert
            type="warning"
            icon={<WarningOutlined />}
            showIcon
            message={t('digitalSignature.agentMissing', 'Không kết nối được VGCASignService')}
            description={
              <span>
                {t(
                  'digitalSignature.installGuide',
                  'Vui lòng cài đặt VGCASignService và cắm USB Token trước khi ký. ',
                )}
                <a
                  href="https://ca.gov.vn/vgca"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  {t('digitalSignature.installGuideLink', 'Tải phần mềm VGCA')}
                </a>
              </span>
            }
          />
        )}

        {/* Agent present but not responding (timeout / error / token not ready) — retry, not install */}
        {agentStatus === 'unreachable' && (
          <Alert
            type="warning"
            icon={<WarningOutlined />}
            showIcon
            message={t('digitalSignature.agentUnreachable', 'Không kết nối được tới agent ký số')}
            description={
              <Space direction="vertical" size="small">
                <span>
                  {t(
                    'digitalSignature.agentUnreachableDesc',
                    'Agent VGCASignService có vẻ chưa khởi động hoặc USB Token chưa sẵn sàng. Vui lòng kiểm tra rồi thử lại.',
                  )}
                </span>
                <Button size="small" onClick={() => setRetryNonce((n) => n + 1)}>
                  {t('common.retry', 'Thử lại')}
                </Button>
              </Space>
            }
          />
        )}

        {/* Async pending notice */}
        {isPending && (
          <Alert
            type="info"
            showIcon
            message={t('digitalSignature.pendingTitle', 'Đang chờ phê duyệt')}
            description={t(
              'digitalSignature.pendingDesc',
              'Yêu cầu ký đã gửi tới thiết bị di động. Vui lòng xác nhận trên ứng dụng SmartCA.',
            )}
          />
        )}

        {/* Sign reason input */}
        {!isPending && (
          <div>
            <Text>{t('digitalSignature.reason', 'Lý do ký')}</Text>
            <Input
              style={{ marginTop: 4 }}
              placeholder={t('digitalSignature.reasonPlaceholder', 'Nhập lý do ký (tuỳ chọn)')}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              maxLength={200}
              disabled={isSigning}
            />
          </div>
        )}

        {/* Error display */}
        {errorMsg && (
          <Alert
            type="error"
            showIcon
            message={t('digitalSignature.failed', 'Ký số thất bại')}
            description={errorMsg}
          />
        )}
      </Space>
    </Modal>
  );
}
