/**
 * sign-uploaded-file-agent-alerts.tsx — agent status UI fragments for the sign-uploaded-PDF modal.
 *
 * Extracted to keep sign-uploaded-file-modal.tsx under 200 lines.
 * Mirrors the exact same agent-status → message mapping used in signing-prompt.tsx
 * (checking spinner, not-installed install guide, unreachable retry).
 */

import { Alert, Button, Space, Spin, Typography } from 'antd';
import { WarningOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { AgentProbeStatus } from '@/features/digital-signature/registry';

const { Text } = Typography;

interface AgentStatusAlertsProps {
  agentStatus: AgentProbeStatus | 'checking' | 'not-required';
  onRetry: () => void;
}

/**
 * Renders the appropriate UI for each agent probe state.
 * Returns null when the agent is available or not-required (no message needed).
 */
export function AgentStatusAlerts({ agentStatus, onRetry }: AgentStatusAlertsProps) {
  const { t } = useTranslation();

  if (agentStatus === 'checking') {
    return (
      <Space>
        <Spin size="small" />
        <Text type="secondary">{t('digitalSignature.agentChecking', 'Đang kiểm tra agent cục bộ...')}</Text>
      </Space>
    );
  }

  if (agentStatus === 'not-installed') {
    return (
      <Alert
        type="warning"
        icon={<WarningOutlined />}
        showIcon
        message={t('digitalSignature.agentMissing', 'Không kết nối được VGCASignService')}
        description={
          <span>
            {t('digitalSignature.installGuide', 'Vui lòng cài đặt VGCASignService và cắm USB Token trước khi ký. ')}
            <a href="https://ca.gov.vn/vgca" target="_blank" rel="noopener noreferrer">
              {t('digitalSignature.installGuideLink', 'Tải phần mềm VGCA')}
            </a>
          </span>
        }
      />
    );
  }

  if (agentStatus === 'unreachable') {
    return (
      <Alert
        type="warning"
        icon={<WarningOutlined />}
        showIcon
        message={t('digitalSignature.agentUnreachable', 'Không kết nối được tới agent ký số')}
        description={
          <Space direction="vertical" size="small">
            <span>
              {t('digitalSignature.agentUnreachableDesc',
                'Agent VGCASignService có vẻ chưa khởi động hoặc USB Token chưa sẵn sàng. Vui lòng kiểm tra rồi thử lại.')}
            </span>
            <Button size="small" onClick={onRetry}>
              {t('common.retry', 'Thử lại')}
            </Button>
          </Space>
        }
      />
    );
  }

  // 'available' and 'not-required' — no alert needed
  return null;
}
