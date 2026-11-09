import { useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Card,
  Col,
  Divider,
  Form,
  Input,
  Modal,
  QRCode,
  Result,
  Row,
  Space,
  Spin,
  Steps,
  Tag,
  Typography,
  Popconfirm,
} from 'antd';
import {
  CheckCircleOutlined,
  CopyOutlined,
  DeleteOutlined,
  KeyOutlined,
  MobileOutlined,
  SafetyCertificateOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/core/api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { appMessage } from '@/core/api/notification-bridge';

const { Text } = Typography;

// ─── Types ───────────────────────────────────────────────────────────────────

interface BeginEnrolData {
  qrCodeUri: string;    // otpauth://totp/... URI
  secretBase32: string; // manual entry key
}

// ─── Steps ───────────────────────────────────────────────────────────────────

// ─── Component ───────────────────────────────────────────────────────────────

/**
 * MfaEnrolmentPage — TOTP enrolment wizard for the current authenticated user.
 * Route: /account/mfa (authenticated, no [RequiresMfa] — user may not have MFA yet).
 *
 * Flow:
 *   Step 1 (install)  — instructions to install an authenticator app
 *   Step 2 (scan)     — GET /api/v1/account/mfa/enrol/totp → show otpauth URI + manual key
 *   Step 3 (verify)   — POST /api/v1/account/mfa/enrol/totp with TOTP code
 *   Step 4 (codes)    — show one-time plaintext recovery codes (must acknowledge before dismiss)
 */
export function MfaEnrolmentPage() {
  const { t } = useTranslation();
  const [currentStep, setCurrentStep] = useState(0);
  const [enrolData, setEnrolData] = useState<BeginEnrolData | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [totpCode, setTotpCode] = useState('');
  const [loading, setLoading] = useState(false);
  const [codesAcknowledged, setCodesAcknowledged] = useState(false);
  const [unenrolLoading, setUnenrolLoading] = useState(false);
  const [enrolDone, setEnrolDone] = useState(false);
  const [statusLoading, setStatusLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await apiClient.get<{ enrolled: boolean; enrolledAt: string | null }>(
          '/account/mfa/status',
        );
        if (!cancelled) setEnrolDone(res.data.enrolled);
      } catch {
        // silent — fall back to wizard view
      } finally {
        if (!cancelled) setStatusLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  // ── Step 1 → 2: Begin enrolment ─────────────────────────────────────────

  async function handleBeginEnrol() {
    setLoading(true);
    try {
      const res = await apiClient.get<BeginEnrolData>('/account/mfa/enrol/totp');
      setEnrolData(res.data);
      setCurrentStep(1);
    } catch {
      appMessage.error(t('mfa.enrol.beginFailed', 'Không thể bắt đầu kích hoạt MFA. Vui lòng thử lại.'));
    } finally {
      setLoading(false);
    }
  }

  // ── Step 2 → 3: Advance to verify ───────────────────────────────────────

  function handleAdvanceToVerify() {
    setCurrentStep(2);
    setTotpCode('');
  }

  // ── Step 3: Complete enrolment ───────────────────────────────────────────

  async function handleCompleteEnrol() {
    if (totpCode.length !== 6 || !/^\d{6}$/.test(totpCode)) {
      appMessage.warning(t('mfa.enrol.invalidCode', 'Mã phải là 6 chữ số.'));
      return;
    }
    setLoading(true);
    try {
      const res = await apiClient.post<{ recoveryCodes: string[] }>(
        '/account/mfa/enrol/totp',
        { totpCode },
      );
      setRecoveryCodes(res.data.recoveryCodes);
      setCurrentStep(3);
    } catch {
      appMessage.error(t('mfa.enrol.verifyFailed', 'Mã không đúng hoặc đã hết hạn. Vui lòng thử lại.'));
    } finally {
      setLoading(false);
    }
  }

  // ── Step 4: Finish after acknowledging codes ─────────────────────────────

  function handleFinish() {
    if (!codesAcknowledged) {
      appMessage.warning(t('mfa.enrol.mustAcknowledge', 'Vui lòng xác nhận đã lưu mã dự phòng.'));
      return;
    }
    setEnrolDone(true);
    setCurrentStep(0);
    setEnrolData(null);
    setRecoveryCodes([]);
    setCodesAcknowledged(false);
    appMessage.success(t('mfa.enrol.success', 'Kích hoạt MFA thành công!'));
  }

  // ── Unenrol (separate action, requires [RequiresMfa] on BE) ─────────────

  async function handleUnenrol() {
    setUnenrolLoading(true);
    try {
      await apiClient.delete('/account/mfa/totp');
      setEnrolDone(false);
      appMessage.success(t('mfa.unenrol.success', 'Đã tắt MFA.'));
    } catch {
      appMessage.error(t('mfa.unenrol.failed', 'Không thể tắt MFA. Phiên MFA có thể đã hết hạn — đăng nhập lại và thử lại.'));
    } finally {
      setUnenrolLoading(false);
    }
  }

  // ── Copy helper ──────────────────────────────────────────────────────────

  function copyToClipboard(text: string, label: string) {
    navigator.clipboard.writeText(text).then(() => {
      appMessage.success(`${label} ${t('mfa.enrol.copied', 'đã sao chép')}`);
    });
  }

  // ─── Step content renderers ───────────────────────────────────────────────

  function renderStep0() {
    return (
      <Space direction="vertical" size="large" style={{ width: '100%' }}>
        <Alert
          type="info"
          showIcon
          message={t('mfa.enrol.step1Title', 'Bước 1: Cài đặt ứng dụng Authenticator')}
          description={t('mfa.enrol.step1Desc',
            'Tải ứng dụng xác thực 2 bước trên điện thoại của bạn: Google Authenticator, Microsoft Authenticator, Authy, hoặc bất kỳ ứng dụng TOTP nào khác.')}
        />
        <Row gutter={16}>
          {[
            { name: 'Google Authenticator', store: 'Play Store / App Store' },
            { name: 'Microsoft Authenticator', store: 'Play Store / App Store' },
            { name: 'Authy', store: 'Play Store / App Store / Desktop' },
          ].map((app) => (
            <Col span={8} key={app.name}>
              <Card size="small" style={{ textAlign: 'center', height: '100%' }}>
                <MobileOutlined style={{ fontSize: 24, color: '#1677ff', marginBottom: 8 }} />
                <div><Text strong>{app.name}</Text></div>
                <Text type="secondary" style={{ fontSize: 12 }}>{app.store}</Text>
              </Card>
            </Col>
          ))}
        </Row>
        <Button type="primary" onClick={handleBeginEnrol} loading={loading} size="large" block>
          {t('mfa.enrol.iHaveApp', 'Tôi đã cài xong — Bắt đầu kích hoạt')}
        </Button>
      </Space>
    );
  }

  function renderStep1() {
    if (!enrolData) return null;
    return (
      <Space direction="vertical" size="large" style={{ width: '100%' }}>
        <Alert
          type="info"
          showIcon
          message={t('mfa.enrol.step2Title', 'Bước 2: Thêm tài khoản vào ứng dụng')}
          description={t('mfa.enrol.step2Desc',
            'Mở ứng dụng Authenticator → Thêm tài khoản → Quét QR bên dưới (khuyến nghị), hoặc nhập thủ công khóa bí mật, hoặc mở đường dẫn trên thiết bị di động.')}
        />

        {/* QR code — primary path for desktop users scanning with phone */}
        <Card size="small">
          <Space direction="vertical" align="center" style={{ width: '100%' }}>
            <Text strong>{t('mfa.enrol.scanQr', 'Quét mã QR bằng ứng dụng Authenticator:')}</Text>
            <QRCode value={enrolData.qrCodeUri} size={200} errorLevel="M" />
            <Text type="secondary" style={{ fontSize: 12 }}>
              {t('mfa.enrol.qrHint', 'Mở ứng dụng → "Thêm tài khoản" → "Quét mã QR".')}
            </Text>
          </Space>
        </Card>

        {/* Manual secret key */}
        <Card size="small">
          <Space direction="vertical" style={{ width: '100%' }}>
            <Text strong>{t('mfa.enrol.manualKey', 'Khóa bí mật (nhập thủ công):')}</Text>
            <Input.Password
              value={enrolData.secretBase32}
              readOnly
              addonAfter={
                <Button
                  type="text"
                  size="small"
                  icon={<CopyOutlined />}
                  onClick={() => copyToClipboard(enrolData.secretBase32, t('mfa.enrol.secretKey', 'Khóa bí mật'))}
                />
              }
              style={{ fontFamily: 'monospace' }}
            />
            <Text type="secondary" style={{ fontSize: 12 }}>
              {t('mfa.enrol.keyFormat', 'Chọn loại "Time-based (TOTP)" khi nhập thủ công.')}
            </Text>
          </Space>
        </Card>

        {/* Mobile deep-link */}
        <Card size="small">
          <Space direction="vertical" style={{ width: '100%' }}>
            <Text strong>{t('mfa.enrol.mobileLink', 'Mở trên thiết bị di động:')}</Text>
            <Button
              href={enrolData.qrCodeUri}
              target="_blank"
              rel="noopener noreferrer"
              icon={<MobileOutlined />}
            >
              {t('mfa.enrol.openInApp', 'Mở ứng dụng Authenticator')}
            </Button>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {t('mfa.enrol.deepLinkNote', 'Nhấn nút trên trên thiết bị di động để tự động thêm vào ứng dụng Authenticator.')}
            </Text>
          </Space>
        </Card>

        <Button type="primary" onClick={handleAdvanceToVerify} size="large" block>
          {t('mfa.enrol.addedAccount', 'Đã thêm tài khoản — Xác minh mã')}
        </Button>
      </Space>
    );
  }

  function renderStep2() {
    return (
      <Space direction="vertical" size="large" style={{ width: '100%' }}>
        <Alert
          type="info"
          showIcon
          message={t('mfa.enrol.step3Title', 'Bước 3: Xác minh mã TOTP')}
          description={t('mfa.enrol.step3Desc',
            'Mở ứng dụng Authenticator, nhìn vào mã 6 chữ số cho tài khoản vừa thêm và nhập vào ô bên dưới.')}
        />
        <Form layout="vertical" onFinish={handleCompleteEnrol}>
          <Form.Item
            label={t('mfa.enrol.totpCode', 'Mã TOTP (6 chữ số)')}
            required
          >
            <Input
              value={totpCode}
              onChange={(e) => setTotpCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
              placeholder="123456"
              maxLength={6}
              style={{ fontFamily: 'monospace', fontSize: 20, letterSpacing: 4, width: 180 }}
              autoComplete="one-time-code"
              inputMode="numeric"
            />
          </Form.Item>
          <Button
            type="primary"
            htmlType="submit"
            loading={loading}
            disabled={totpCode.length !== 6}
            size="large"
          >
            {t('mfa.enrol.verify', 'Xác minh và hoàn tất')}
          </Button>
        </Form>
      </Space>
    );
  }

  function renderStep3() {
    return (
      <Space direction="vertical" size="large" style={{ width: '100%' }}>
        <Alert
          type="warning"
          showIcon
          icon={<KeyOutlined />}
          message={t('mfa.enrol.codesTitle', 'Mã dự phòng — Lưu ngay bây giờ!')}
          description={t('mfa.enrol.codesDesc',
            'Mỗi mã chỉ dùng được 1 lần. Nếu mất điện thoại, dùng mã này để đăng nhập. Sau khi đóng cửa sổ này, mã sẽ không hiển thị lại.')}
        />

        <Card size="small" style={{ background: '#f6ffed', borderColor: '#b7eb8f' }}>
          <Space direction="vertical" style={{ width: '100%' }}>
            <Space style={{ justifyContent: 'space-between', width: '100%' }}>
              <Text strong>{t('mfa.enrol.recoveryCodes', 'Mã dự phòng (16 mã):')}</Text>
              <Button
                size="small"
                icon={<CopyOutlined />}
                onClick={() => copyToClipboard(recoveryCodes.join('\n'), t('mfa.enrol.allCodes', 'Tất cả mã'))}
              >
                {t('mfa.enrol.copyAll', 'Sao chép tất cả')}
              </Button>
            </Space>
            <Row gutter={[8, 8]}>
              {recoveryCodes.map((code) => (
                <Col span={12} key={code}>
                  <code
                    style={{
                      display: 'block',
                      padding: '4px 8px',
                      background: '#fff',
                      border: '1px solid #d9d9d9',
                      borderRadius: 4,
                      fontFamily: 'monospace',
                      fontSize: 13,
                      letterSpacing: 1,
                    }}
                  >
                    {code}
                  </code>
                </Col>
              ))}
            </Row>
          </Space>
        </Card>

        <Alert
          type="error"
          message={
            <Space>
              <input
                type="checkbox"
                checked={codesAcknowledged}
                onChange={(e) => setCodesAcknowledged(e.target.checked)}
                id="codes-ack"
              />
              <label htmlFor="codes-ack" style={{ cursor: 'pointer' }}>
                {t('mfa.enrol.ackLabel', 'Tôi đã lưu các mã dự phòng vào nơi an toàn.')}
              </label>
            </Space>
          }
        />

        <Button
          type="primary"
          onClick={handleFinish}
          disabled={!codesAcknowledged}
          size="large"
          icon={<CheckCircleOutlined />}
          block
        >
          {t('mfa.enrol.finish', 'Hoàn tất kích hoạt MFA')}
        </Button>
      </Space>
    );
  }

  // ─── Main render ──────────────────────────────────────────────────────────

  const stepItems = [
    { title: t('mfa.enrol.stepInstall', 'Cài ứng dụng') },
    { title: t('mfa.enrol.stepScan', 'Thêm tài khoản') },
    { title: t('mfa.enrol.stepVerify', 'Xác minh') },
    { title: t('mfa.enrol.stepCodes', 'Mã dự phòng') },
  ];

  const stepContent = [renderStep0, renderStep1, renderStep2, renderStep3];

  return (
    <div>
      <AdminPageHeader
        title={t('mfa.pageTitle', 'Xác thực hai yếu tố (MFA)')}
      />

      <AdminContentCard>
        {statusLoading ? (
          <div style={{ textAlign: 'center', padding: 48 }}>
            <Spin size="large" />
          </div>
        ) : enrolDone ? (
          // ── Already enrolled — show status + management options ────────────
          <Space direction="vertical" size="large" style={{ width: '100%' }}>
            <Result
              status="success"
              icon={<SafetyCertificateOutlined />}
              title={t('mfa.status.active', 'MFA đang hoạt động')}
              subTitle={t('mfa.status.activeDesc', 'Tài khoản của bạn được bảo vệ bằng xác thực hai yếu tố.')}
            />

            <Divider>{t('mfa.management.title', 'Quản lý MFA')}</Divider>

            <Space wrap>
              <Popconfirm
                title={t('mfa.unenrol.confirm', 'Tắt MFA sẽ làm giảm bảo mật tài khoản. Xác nhận tắt?')}
                onConfirm={handleUnenrol}
                okText={t('common.confirm', 'Xác nhận')}
                cancelText={t('common.cancel', 'Hủy')}
                okButtonProps={{ danger: true }}
              >
                <Button
                  danger
                  icon={<DeleteOutlined />}
                  loading={unenrolLoading}
                >
                  {t('mfa.unenrol.button', 'Tắt MFA')}
                </Button>
              </Popconfirm>

              <RegenerateCodesButton />
            </Space>
          </Space>
        ) : (
          // ── Not enrolled — show wizard ────────────────────────────────────
          <Space direction="vertical" size="large" style={{ width: '100%' }}>
            <Alert
              type="info"
              showIcon
              message={
                <Space>
                  {t('mfa.notEnrolled.title', 'MFA chưa được kích hoạt')}
                  <Tag color="orange">{t('mfa.notEnrolled.recommended', 'Khuyến nghị')}</Tag>
                </Space>
              }
              description={t('mfa.notEnrolled.desc',
                'Kích hoạt xác thực hai yếu tố (MFA) để bảo vệ tài khoản khỏi truy cập trái phép.')}
            />

            <Steps current={currentStep} items={stepItems} style={{ marginBottom: 24 }} />

            {stepContent[currentStep]?.()}
          </Space>
        )}
      </AdminContentCard>
    </div>
  );
}

// ─── RegenerateCodesButton — inline sub-component ────────────────────────────

function RegenerateCodesButton() {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(false);
  const [newCodes, setNewCodes] = useState<string[] | null>(null);

  async function handleRegenerate() {
    setLoading(true);
    try {
      const res = await apiClient.post<{ recoveryCodes: string[] }>(
        '/account/mfa/recovery-codes/regenerate',
      );
      setNewCodes(res.data.recoveryCodes);
    } catch {
      appMessage.error(t('mfa.codes.regenerateFailed',
        'Không thể tạo mã mới. Phiên MFA có thể đã hết hạn — đăng nhập lại và thử lại.'));
    } finally {
      setLoading(false);
    }
  }

  function copyAll() {
    if (!newCodes) return;
    navigator.clipboard.writeText(newCodes.join('\n')).then(() => {
      appMessage.success(t('mfa.enrol.copied', 'đã sao chép'));
    });
  }

  return (
    <>
      <Popconfirm
        title={t('mfa.codes.regenerateConfirm', 'Tạo mã mới sẽ vô hiệu hóa tất cả mã hiện tại. Xác nhận?')}
        onConfirm={handleRegenerate}
        okText={t('common.confirm', 'Xác nhận')}
        cancelText={t('common.cancel', 'Hủy')}
      >
        <Button icon={<KeyOutlined />} loading={loading}>
          {t('mfa.codes.regenerate', 'Tạo mã dự phòng mới')}
        </Button>
      </Popconfirm>

      {/* One-time display modal for newly generated codes */}
      <Modal
        title={t('mfa.codes.newCodesTitle', 'Mã dự phòng mới')}
        open={!!newCodes}
        onOk={() => setNewCodes(null)}
        onCancel={() => setNewCodes(null)}
        okText={t('common.close', 'Đóng')}
        cancelButtonProps={{ style: { display: 'none' } }}
        destroyOnHidden
        width={480}
      >
        {newCodes && (
          <Space direction="vertical" style={{ width: '100%' }}>
            <Alert
              type="warning"
              showIcon
              message={t('mfa.codes.saveNow', 'Lưu ngay — không hiển thị lại!')}
            />
            <Button size="small" icon={<CopyOutlined />} onClick={copyAll}>
              {t('mfa.enrol.copyAll', 'Sao chép tất cả')}
            </Button>
            <Row gutter={[8, 8]}>
              {newCodes.map((code) => (
                <Col span={12} key={code}>
                  <code
                    style={{
                      display: 'block',
                      padding: '4px 8px',
                      background: '#f5f5f5',
                      border: '1px solid #d9d9d9',
                      borderRadius: 4,
                      fontFamily: 'monospace',
                      fontSize: 13,
                    }}
                  >
                    {code}
                  </code>
                </Col>
              ))}
            </Row>
          </Space>
        )}
      </Modal>
    </>
  );
}
