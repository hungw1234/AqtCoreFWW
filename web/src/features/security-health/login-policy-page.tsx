// login-policy-page.tsx — QĐ 742 §1.4.b/1.4.d D7 visibility: login/lockout policy snapshot.
// Read-only audit walkthrough evidence page.
import { Alert, Descriptions, Spin, Tag, Typography } from 'antd';
import { CheckCircleOutlined, CloseCircleOutlined, LockOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useLoginPolicyStatus } from './security-health-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

// LoginPolicyPage — D7 compliance snapshot for QĐ 742 §1.4.b login warning + lockout settings.
export function LoginPolicyPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError, dataUpdatedAt } = useLoginPolicyStatus();

  const title = t('securityHealth.loginPolicy.title', 'Chính sách đăng nhập & Khoá tài khoản');

  return (
    <div>
      <AdminPageHeader
        title={title}
        icon={<LockOutlined />}
        description={t(
          'securityHealth.loginPolicy.qd742Wording',
          'Hiển thị cảnh báo và ghi nhật ký các lần đăng nhập sai',
        )}
      />

      <AdminContentCard>
        {isLoading && (
          <div style={{ textAlign: 'center', padding: 48 }}>
            <Spin size="large" />
          </div>
        )}

        {isError && !isLoading && (
          <Alert
            type="error"
            showIcon
            message={t('common.error', 'Lỗi')}
            description={t('securityHealth.loadError', 'Không thể tải dữ liệu. Vui lòng thử lại.')}
          />
        )}

        {!isLoading && !isError && data && (
          <>
            {!data.hasDefaultPolicy && (
              <Alert
                type="warning"
                showIcon
                style={{ marginBottom: 16 }}
                message={t(
                  'securityHealth.loginPolicy.noPolicyWarning',
                  'Chưa có chính sách mặc định cho tenant này. Hệ thống dùng giá trị framework mặc định.',
                )}
              />
            )}

            <Descriptions
              bordered
              size="small"
              column={1}
              labelStyle={{ width: 280, fontWeight: 500 }}
            >
              <Descriptions.Item label={t('securityHealth.loginPolicy.policyName', 'Tên chính sách')}>
                {data.policyName ?? <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.loginPolicy.maxFailedAttempts', 'Số lần thử tối đa')}>
                <Tag color="blue">{data.maxFailedAttempts}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.loginPolicy.lockoutMinutes', 'Thời gian khoá (phút)')}>
                <Tag color="orange">{data.lockoutMinutes}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.loginPolicy.showRemainingAttempts', 'Hiển thị số lần thử còn lại')}>
                {data.showRemainingAttempts
                  ? <Tag icon={<CheckCircleOutlined />} color="success">{t('common.yes', 'Có')}</Tag>
                  : <Tag icon={<CloseCircleOutlined />} color="default">{t('common.no', 'Không')}</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.loginPolicy.escalationLockoutCount', 'Số lần khoá liên tiếp để escalate (QĐ 742 §1.4.d)')}>
                <Tag color="red">{data.escalationLockoutCount}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.loginPolicy.escalationWindowHours', 'Cửa sổ thời gian escalation (giờ)')}>
                <Tag color="purple">{data.escalationWindowHours}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.loginPolicy.hasDefaultPolicy', 'Trạng thái chính sách mặc định')}>
                {data.hasDefaultPolicy
                  ? <Tag icon={<CheckCircleOutlined />} color="success">{t('securityHealth.configured', 'Đã cấu hình')}</Tag>
                  : <Tag icon={<CloseCircleOutlined />} color="error">{t('securityHealth.notConfigured', 'Chưa cấu hình')}</Tag>}
              </Descriptions.Item>
            </Descriptions>
          </>
        )}

        {dataUpdatedAt > 0 && (
          <Text type="secondary" style={{ display: 'block', marginTop: 16, fontSize: 12 }}>
            {t('securityHealth.lastRefreshedAt', 'Cập nhật lúc')}:{' '}
            {dayjs(dataUpdatedAt).format('DD/MM/YYYY HH:mm:ss')}
          </Text>
        )}
      </AdminContentCard>
    </div>
  );
}
