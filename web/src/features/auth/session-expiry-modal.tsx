// session-expiry-modal.tsx — idle-timeout warning modal (user still active)
// Silent renew failures use a toast countdown in AuthProvider instead — the user can't
// do anything useful once renew fails, so a modal click is pure friction.

import { useEffect, useState } from 'react';
import { Modal, Button, Typography } from 'antd';
import { ClockCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';

const { Text, Title } = Typography;

interface SessionExpiryModalProps {
  open: boolean;
  countdownSeconds: number;
  /** Extend session — resets idle timer + renews token */
  onExtend: () => void;
  /** Countdown reached 0 — auto logout */
  onTimeout: () => void;
}

export function SessionExpiryModal({
  open, countdownSeconds, onExtend, onTimeout,
}: SessionExpiryModalProps) {
  const { t } = useTranslation();
  const [remaining, setRemaining] = useState(countdownSeconds);

  useEffect(() => {
    if (open) setRemaining(countdownSeconds);
  }, [open, countdownSeconds]);

  useEffect(() => {
    if (!open) return;
    const id = setInterval(() => {
      setRemaining((prev) => {
        if (prev <= 1) { clearInterval(id); onTimeout(); return 0; }
        return prev - 1;
      });
    }, 1000);
    return () => clearInterval(id);
  }, [open, onTimeout]);

  return (
    <Modal open={open} closable={false} maskClosable={false} keyboard={false}
      footer={[
        <Button key="extend" type="primary" size="large" onClick={onExtend}>
          {t('auth.session.extend', 'Gia hạn')}
        </Button>,
      ]} width={420} centered>
      <div style={{ textAlign: 'center', padding: '16px 0' }}>
        <ClockCircleOutlined style={{ fontSize: 48, color: '#faad14', marginBottom: 16 }} />
        <Title level={4} style={{ margin: 0 }}>
          {t('auth.session.expiringTitle', 'Phiên sắp hết hạn')}
        </Title>
        <Text type="secondary" style={{ display: 'block', margin: '12px 0 20px' }}>
          {t('auth.session.idleDesc', 'Phiên làm việc sẽ tự động đăng xuất sau {{seconds}} giây do không có thao tác.', { seconds: remaining })}
        </Text>
        <Title level={1} style={{ margin: 0, color: remaining <= 10 ? '#ff4d4f' : '#faad14' }}>
          {remaining}
        </Title>
      </div>
    </Modal>
  );
}
