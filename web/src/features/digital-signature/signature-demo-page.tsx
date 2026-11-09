/**
 * signature-demo-page.tsx — User-facing demo: sign a sample payload then verify it.
 * Route: /digital-signature/demo (authenticated, no admin perm required).
 * Useful for users to self-test their tenant's signing setup.
 *
 * Field mapping verified against BE 2026-05-11:
 * - provider/enabled read from GET /digital-signature/provider-info (JWT-scoped): { isEnabled, providerKey, capabilities }
 * - VerifyResponse: { isValid, failureReason, signingCertificate, chainResult }
 *   signerCn derived from signingCertificate.subject (parse CN= from DN)
 *   notBefore/notAfter from signingCertificate
 *   ocspStatus derived from chainResult.ocspChecked + ocspSucceeded
 */

import { useState } from 'react';
import { Button, Input, Alert, Space, Typography, Divider, Tag, Spin, Card } from 'antd';
import { CenteredSpin } from '@/shared/components/centered-spin';
import { SafetyCertificateOutlined, CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { uint8ToBase64 } from './base64-utils';
import { useSigningProviderInfo, callVerify } from './signature-api';
import { SigningPrompt } from './signing-prompt';
import { PadesOrchestratorCard } from './pades-orchestrator-card';
import { DocFlowUsbCard } from './docflow-usb-card';
import type { VerifyResponse, SignResult } from './signature-types';

const { Title, Text, Paragraph } = Typography;
const { TextArea } = Input;

// ─── Sample payload ───────────────────────────────────────────────────────────

const DEFAULT_SAMPLE = 'Tài liệu mẫu — QĐ742 demo ký số điện tử — AqtCoreFW';

// ─── Helpers ──────────────────────────────────────────────────────────────────

/**
 * Extract CN value from an X.500 Distinguished Name string.
 * e.g. "CN=Nguyen Van A, O=VNPT, C=VN" → "Nguyen Van A"
 * Returns the full subject string if CN= is not found.
 */
function parseCn(subject: string): string {
  const match = /(?:^|,\s*)CN=([^,]+)/i.exec(subject);
  return match ? match[1].trim() : subject;
}

/**
 * Derive a human-readable OCSP status string from chainResult booleans.
 * Mirrors the logic in the BE ChainValidationResult record.
 */
function ocspStatusLabel(
  ocspChecked: boolean,
  ocspSucceeded: boolean,
  t: (key: string, fallback: string) => string,
): string {
  if (!ocspChecked) return t('digitalSignature.ocspNotChecked', 'Không kiểm tra');
  if (ocspSucceeded) return t('digitalSignature.ocspGood', 'Hợp lệ');
  return t('digitalSignature.ocspFailed', 'Thất bại');
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * SignatureDemoPage — lets authenticated users test digital signing end-to-end.
 * Signs a text payload and immediately verifies the resulting signature.
 *
 * Config is read via useSigningProviderInfo (JWT-tenant-scoped, GET /digital-signature/provider-info):
 * any authenticated user can call it — matching this route's "no admin permission required" contract.
 * The admin-only useSignatureConfig (which needs a tenantId path param) would leave non-admin users with
 * isEnabled=false and a fully disabled page. provider-info also returns the real capability string, so the
 * SigningPrompt can run its local-agent probe (USB-VGCA) instead of being fed an empty capabilities string.
 */
export function SignatureDemoPage() {
  const { t } = useTranslation();

  const { data: providerInfo, isLoading: configLoading } = useSigningProviderInfo();

  const [sampleText, setSampleText] = useState(DEFAULT_SAMPLE);
  const [signingOpen, setSigningOpen] = useState(false);
  const [signResult, setSignResult] = useState<SignResult | null>(null);
  const [isVerifying, setIsVerifying] = useState(false);
  const [verifyResult, setVerifyResult] = useState<VerifyResponse | null>(null);
  const [verifyError, setVerifyError] = useState<string | null>(null);

  // Encode current text to Uint8Array for signing
  const payloadBytes = new TextEncoder().encode(sampleText);

  // ── Handlers ──────────────────────────────────────────────────────────────

  function handleOpenSigning() {
    setSignResult(null);
    setVerifyResult(null);
    setVerifyError(null);
    setSigningOpen(true);
  }

  function handleSignSuccess(result: SignResult) {
    setSignResult(result);
    setSigningOpen(false);
  }

  async function handleVerify() {
    if (!signResult) return;
    setIsVerifying(true);
    setVerifyResult(null);
    setVerifyError(null);
    try {
      // Encode text payload to base64 — matches BE VerifyRequest.Payload (byte[])
      const payloadBase64 = uint8ToBase64(payloadBytes);
      // A separately-returned signer cert ⇒ raw RSA/ECDSA signature (VGCA + Viettel).
      // Send format='Raw' + the cert so BE skips the CMS parser.
      const isRaw = !!signResult.signerCertBase64;
      const result = await callVerify({
        payload: payloadBase64,
        signature: signResult.signatureBase64,
        // Omit ocspModeOverride → BE applies the tenant's configured OcspMode by default (same effective policy;
        // provider-info does not expose ocspMode, and an explicit override equal to the default is a no-op).
        ...(isRaw && {
          format: 'Raw' as const,
          signerCertBase64: signResult.signerCertBase64,
          hashAlg: signResult.hashAlg,
          encode: signResult.encode,
        }),
      });
      setVerifyResult(result);
    } catch (err) {
      setVerifyError(err instanceof Error ? err.message : t('digitalSignature.failed', 'Xác minh thất bại'));
    } finally {
      setIsVerifying(false);
    }
  }

  // ── Derived state ─────────────────────────────────────────────────────────

  const isEnabled = providerInfo?.isEnabled ?? false;
  const providerKey = providerInfo?.providerKey ?? null;
  // Real capability string (comma-separated [Flags] names) — drives the SigningPrompt local-agent probe.
  const capabilities = providerInfo?.capabilities ?? '';

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div style={{ maxWidth: 720, margin: '0 auto', padding: '24px 16px' }}>
      <Space direction="vertical" style={{ width: '100%' }} size="large">

        {/* Header */}
        <div>
          <Title level={3} style={{ marginBottom: 4 }}>
            <SafetyCertificateOutlined style={{ marginRight: 8 }} />
            {t('digitalSignature.demoTitle', 'Demo ký số điện tử')}
          </Title>
          <Paragraph type="secondary">
            {t(
              'digitalSignature.demoDesc',
              'Nhập nội dung văn bản, ký số bằng provider của tenant, rồi xác minh chữ ký.',
            )}
          </Paragraph>
        </div>

        {/* Config status */}
        {configLoading ? (
          <Spin />
        ) : !isEnabled ? (
          <Alert
            type="warning"
            showIcon
            message={t('digitalSignature.notEnabled', 'Chữ ký số chưa được kích hoạt cho tenant này.')}
          />
        ) : (
          <Alert
            type="success"
            showIcon
            message={
              <Space>
                {t('digitalSignature.enabledWith', 'Đang dùng provider:')}
                <Tag color="blue">{providerKey ?? '—'}</Tag>
              </Space>
            }
          />
        )}

        {/* Step 1: Input */}
        <Card
          title={`${t('digitalSignature.step1', 'Bước 1')}: ${t('digitalSignature.enterPayload', 'Nhập nội dung cần ký')}`}
          size="small"
        >
          <TextArea
            rows={4}
            value={sampleText}
            onChange={(e) => setSampleText(e.target.value)}
            maxLength={2000}
            showCount
            disabled={!isEnabled}
          />
          <div style={{ marginTop: 12 }}>
            <Button
              type="primary"
              icon={<SafetyCertificateOutlined />}
              onClick={handleOpenSigning}
              disabled={!isEnabled || !sampleText.trim()}
            >
              {t('digitalSignature.sign', 'Ký số')}
            </Button>
          </div>
        </Card>

        {/* DEV-only: full production remote-PAdES path through the orchestrator (upload → request → poll job →
            download). Gated on import.meta.env.DEV — unlike the spike runner it calls PRODUCTION endpoints with no
            server-side dev guard, so it must not render in a production build. */}
        {isEnabled && import.meta.env.DEV && <PadesOrchestratorCard />}

        {/* DEV-only: full production USB-DocFlow path (local agent / token PIN). Gated on import.meta.env.DEV like the
            remote card — it calls production endpoints and drives the VGCASignService desktop plugin for the ceremony. */}
        {isEnabled && import.meta.env.DEV && <DocFlowUsbCard />}

        {/* Step 2: Signature output */}
        {signResult && (
          <Card
            title={`${t('digitalSignature.step2', 'Bước 2')}: ${t('digitalSignature.signatureOutput', 'Chữ ký')}`}
            size="small"
          >
            <Space direction="vertical" style={{ width: '100%' }}>
              <Space>
                <CheckCircleOutlined style={{ color: '#52c41a' }} />
                <Text type="success">{t('digitalSignature.success', 'Ký số thành công')}</Text>
                {signResult.providerKey && <Tag color="blue">{signResult.providerKey}</Tag>}
              </Space>
              <TextArea
                rows={3}
                value={signResult.signatureBase64}
                readOnly
                style={{ fontFamily: 'monospace', fontSize: 11 }}
              />
              <Button onClick={handleVerify} loading={isVerifying}>
                {t('digitalSignature.verify', 'Xác minh chữ ký')}
              </Button>
            </Space>
          </Card>
        )}

        {/* Step 3: Verify result */}
        {isVerifying && <CenteredSpin tip={t('digitalSignature.verifying', 'Đang xác minh...')} />}

        {verifyError && (
          <Alert
            type="error"
            showIcon
            icon={<CloseCircleOutlined />}
            message={t('digitalSignature.verifyFailed', 'Xác minh thất bại')}
            description={verifyError}
          />
        )}

        {verifyResult && (
          <Card
            title={`${t('digitalSignature.step3', 'Bước 3')}: ${t('digitalSignature.verifyResult', 'Kết quả xác minh')}`}
            size="small"
          >
            <Space direction="vertical">
              {verifyResult.isValid ? (
                <Alert
                  type="success"
                  showIcon
                  icon={<CheckCircleOutlined />}
                  message={t('digitalSignature.signatureValid', 'Chữ ký hợp lệ')}
                />
              ) : (
                <Alert
                  type="error"
                  showIcon
                  icon={<CloseCircleOutlined />}
                  message={t('digitalSignature.signatureInvalid', 'Chữ ký không hợp lệ')}
                  // BE field: failureReason (not errorMessage)
                  description={verifyResult.failureReason}
                />
              )}

              {/* Signer CN: parse from signingCertificate.subject */}
              {verifyResult.signingCertificate && (
                <Text>
                  {t('digitalSignature.signerCn', 'Người ký')}:{' '}
                  <strong>{parseCn(verifyResult.signingCertificate.subject)}</strong>
                </Text>
              )}

              {/* Cert validity: from signingCertificate.notBefore / notAfter */}
              {verifyResult.signingCertificate && (
                <Text type="secondary">
                  {t('digitalSignature.certValidity', 'Hiệu lực chứng chỉ')}:{' '}
                  {new Date(verifyResult.signingCertificate.notBefore).toLocaleDateString('vi-VN')}
                  {' — '}
                  {new Date(verifyResult.signingCertificate.notAfter).toLocaleDateString('vi-VN')}
                </Text>
              )}

              {/* OCSP status: derived from chainResult booleans */}
              {verifyResult.chainResult && (
                <Text>
                  {t('digitalSignature.ocspStatus', 'Trạng thái OCSP')}:{' '}
                  <Tag>
                    {ocspStatusLabel(
                      verifyResult.chainResult.ocspChecked,
                      verifyResult.chainResult.ocspSucceeded,
                      t,
                    )}
                  </Tag>
                </Text>
              )}
            </Space>
          </Card>
        )}

        <Divider />
        <Text type="secondary" style={{ fontSize: 12 }}>
          {t(
            'digitalSignature.demoNote',
            'Trang demo — không dùng cho tài liệu pháp lý. Dữ liệu ký chỉ tồn tại trong phiên làm việc.',
          )}
        </Text>

      </Space>

      {/* Signing prompt modal */}
      <SigningPrompt
        open={signingOpen}
        providerKey={providerKey}
        capabilities={capabilities}
        payload={payloadBytes}
        reason="Demo signing — QĐ742"
        onSuccess={handleSignSuccess}
        onCancel={() => setSigningOpen(false)}
      />
    </div>
  );
}
