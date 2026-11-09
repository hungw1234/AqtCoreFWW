/**
 * enroll-cert-modal.tsx — self-service signing-certificate enrollment (Phase 6 Slice B).
 *
 * Multi-step (provider-first — Phase 4 re-order):
 *   1. Pick provider — Remote (Viettel Mysign) or USB token (VGCA).
 *   2. CCCD (only when RemoteViettelMysign and nationalIdEstablished is false) — Mysign requires it.
 *      USB path skips this step entirely.
 *   3a. Remote — issue a challenge, then a single `enroll` POST that blocks server-side on the phone OTP.
 *   3b. USB — issue a challenge, sign the raw nonce on the token (vgca adapter), POST cert + signed nonce.
 *   4. Done — the overview is invalidated so the new Pending enrollment row appears.
 *
 * Identity is server-resolved from the JWT; this modal never sends a user id. The USB branch reuses the
 * existing `usb-vgca` adapter (probe + sign) — the same ceremony as the USB signing path.
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { Modal, Steps, Form, Input, Button, Space, Alert, Spin, Typography, Result } from 'antd';
import { SafetyCertificateOutlined, MobileOutlined, UsbOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { extractApiErrorMessage } from '@/core/api/extract-api-error';
import { resolveAdapter } from '@/features/digital-signature/registry';
// Side-effect: register the VGCA USB adapter before resolveAdapter('usb-vgca').
import '@/features/digital-signature/providers/vgca-usb-adapter';
import { useSetSigningNationalId, useIssueEnrollmentChallenge, useEnrollSignatureCert } from './signatures-api';
import type { EnrollmentKind } from './signature-types';

const { Text } = Typography;

const USB_PROVIDER_KEY = 'usb-vgca';

export interface EnrollCertModalProps {
  open: boolean;
  /** From the overview — when false, the Remote path will collect CCCD before enrollment. */
  nationalIdEstablished: boolean;
  onClose: () => void;
}

type Step = 'pick' | 'cccd' | 'working' | 'done';

/** base64 → raw bytes (the challenge is signed as raw nonce; the adapter hashes internally, as the BE expects). */
function base64ToBytes(b64: string): Uint8Array {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

export function EnrollCertModal({ open, nationalIdEstablished, onClose }: EnrollCertModalProps) {
  const { t } = useTranslation();
  const setCccd = useSetSigningNationalId();
  const issueChallenge = useIssueEnrollmentChallenge();
  const enroll = useEnrollSignatureCert();

  const [step, setStep] = useState<Step>('pick');
  const [cccd, setCccdValue] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [workingKind, setWorkingKind] = useState<EnrollmentKind | null>(null);
  // pendingKind: set when Remote is clicked but CCCD is needed — remembers which provider triggered the CCCD step.
  const [pendingKind, setPendingKind] = useState<EnrollmentKind | null>(null);
  const prevOpenRef = useRef(false);

  // Reset ONLY on an open false→true transition — NOT whenever nationalIdEstablished changes. When the user
  // sets CCCD mid-flow, the overview refetch flips nationalIdEstablished true, but the guard prevents this
  // from re-triggering and wiping the in-progress enrollment state (the prevOpenRef guard from Slice B M1 fix).
  // Provider-first (Phase 4): always open at 'pick' — USB skips CCCD entirely; Remote redirects to CCCD if needed.
  useEffect(() => {
    if (open && !prevOpenRef.current) {
      setStep('pick');
      setCccdValue('');
      setError(null);
      setWorkingKind(null);
      setPendingKind(null);
    }
    prevOpenRef.current = open;
  }, [open]);

  const runEnroll = useCallback(
    async (kind: EnrollmentKind) => {
      setError(null);
      setWorkingKind(kind);
      setStep('working');
      try {
        if (kind === 'UsbTokenVgca') {
          // Token ceremony first (probe + sign), then submit cert + signed nonce.
          const adapter = resolveAdapter(USB_PROVIDER_KEY);
          const status = adapter.probeAgentStatus ? await adapter.probeAgentStatus() : 'available';
          if (status !== 'available') {
            setError(t('profile.signatures.enroll.usbUnavailable', 'Không kết nối được trình ký USB (VGCA). Kiểm tra plugin/đầu đọc token.'));
            setStep('pick');
            return;
          }
          const challenge = await issueChallenge.mutateAsync();
          const signed = await adapter.sign(base64ToBytes(challenge.challengeBase64), { encode: 'RSA' });
          if (!signed.signerCertBase64) {
            setError(t('profile.signatures.enroll.usbNoCert', 'Token không trả về chứng thư số.'));
            setStep('pick');
            return;
          }
          await enroll.mutateAsync({
            kind,
            nonceId: challenge.nonceId,
            signedNonceBase64: signed.signatureBase64,
            certBase64: signed.signerCertBase64,
          });
        } else {
          // Remote: the single enroll POST blocks on the phone OTP (server-side possession proof).
          const challenge = await issueChallenge.mutateAsync();
          await enroll.mutateAsync({ kind, nonceId: challenge.nonceId });
        }
        setStep('done');
      } catch (e) {
        setError(extractApiErrorMessage(e) ?? t('profile.signatures.enroll.enrollError', 'Đăng ký chứng thư thất bại'));
        setStep('pick');
      }
    },
    [issueChallenge, enroll, t],
  );

  const handleSetCccd = useCallback(async () => {
    setError(null);
    try {
      await setCccd.mutateAsync({ nationalId: cccd.trim() });
      // If we arrived here because Remote was clicked with !nationalIdEstablished, proceed to enrollment.
      // Otherwise fall back to pick (shouldn't happen in normal provider-first flow).
      if (pendingKind) {
        const kind = pendingKind;
        setPendingKind(null);
        void runEnroll(kind);
      } else {
        setStep('pick');
      }
    } catch (e) {
      setError(extractApiErrorMessage(e) ?? t('profile.signatures.enroll.cccdError', 'Không thiết lập được CCCD'));
    }
  }, [cccd, setCccd, t, pendingKind, runEnroll]);

  // Provider-first: pick=0, cccd=1 (conditional Remote sub-step), working/done=2
  const stepIndex = step === 'pick' ? 0 : step === 'cccd' ? 1 : 2;

  return (
    <Modal
      open={open}
      title={<Space><SafetyCertificateOutlined />{t('profile.signatures.enroll.title', 'Đăng ký chữ ký số')}</Space>}
      onCancel={onClose}
      footer={step === 'done' ? <Button type="primary" onClick={onClose}>{t('common.close', 'Đóng')}</Button> : null}
      destroyOnHidden
      width={560}
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        <Steps
          size="small"
          current={stepIndex}
          items={[
            { title: t('profile.signatures.enroll.stepProvider', 'Chọn nhà cung cấp') },
            { title: t('profile.signatures.enroll.stepCccd', 'CCCD') },
            { title: t('profile.signatures.enroll.stepConfirm', 'Xác nhận') },
          ]}
        />

        {error && <Alert type="error" showIcon message={error} />}

        {step === 'pick' && (
          <Space direction="vertical" style={{ width: '100%' }} size="small">
            <Text type="secondary">{t('profile.signatures.enroll.pickHint', 'Chọn phương thức đăng ký chứng thư số:')}</Text>
            <Button
              block
              size="large"
              icon={<MobileOutlined />}
              onClick={() => {
                if (!nationalIdEstablished) {
                  // Mysign requires CCCD — collect it first, then proceed to enrollment.
                  setPendingKind('RemoteViettelMysign');
                  setStep('cccd');
                } else {
                  void runEnroll('RemoteViettelMysign');
                }
              }}
            >
              {t('profile.signatures.enroll.remote', 'Ký số từ xa (Viettel Mysign)')}
            </Button>
            <Button
              block
              size="large"
              icon={<UsbOutlined />}
              onClick={() => void runEnroll('UsbTokenVgca')}
            >
              {t('profile.signatures.enroll.usb', 'USB Token (VGCA)')}
            </Button>
          </Space>
        )}

        {step === 'cccd' && (
          <Form layout="vertical" size="small">
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              message={t('profile.signatures.enroll.cccdHint', 'Nhập số CCCD (12 chữ số) để liên kết với chứng thư số. Sau khi có chứng thư hiệu lực sẽ không đổi được.')}
            />
            <Form.Item label={t('profile.signatures.enroll.cccdLabel', 'Số CCCD')} style={{ marginBottom: 12 }}>
              <Input
                value={cccd}
                onChange={(e) => setCccdValue(e.target.value)}
                maxLength={12}
                placeholder="079201001234"
                inputMode="numeric"
              />
            </Form.Item>
            <Button
              type="primary"
              loading={setCccd.isPending}
              disabled={!/^\d{12}$/.test(cccd.trim())}
              onClick={handleSetCccd}
            >
              {t('common.continue', 'Tiếp tục')}
            </Button>
          </Form>
        )}

        {step === 'working' && (
          <Space direction="vertical" align="center" style={{ width: '100%', padding: '16px 0' }}>
            <Spin />
            <Text type="secondary">
              {workingKind === 'RemoteViettelMysign'
                ? t('profile.signatures.enroll.remoteWait', 'Vui lòng mở ứng dụng Mysign trên điện thoại và xác nhận OTP...')
                : t('profile.signatures.enroll.usbWait', 'Vui lòng xác nhận trên token USB (nhập PIN)...')}
            </Text>
          </Space>
        )}

        {step === 'done' && (
          <Result
            status="success"
            title={t('profile.signatures.enroll.success', 'Đã gửi đăng ký chứng thư số')}
            subTitle={t('profile.signatures.enroll.successDesc', 'Chứng thư đang chờ quản trị viên phê duyệt. Bạn sẽ thấy trạng thái trong danh sách bên dưới.')}
          />
        )}
      </Space>
    </Modal>
  );
}
