// password-policy-page.tsx — QĐ 742 §1.5.a/1.5.b D7 visibility: password complexity snapshot.
// Shows both ASP.NET Identity framework defaults and project CredentialPolicy overrides.
import { Alert, Descriptions, Divider, Spin, Tag, Typography } from 'antd';
import { CheckCircleOutlined, CloseCircleOutlined, SafetyOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { usePasswordPolicyStatus } from './security-health-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

// BoolTag — renders a boolean field as a green/grey tag.
function BoolTag({ value }: { value: boolean }) {
  const { t } = useTranslation();
  return value
    ? <Tag icon={<CheckCircleOutlined />} color="success">{t('common.yes', 'Có')}</Tag>
    : <Tag icon={<CloseCircleOutlined />} color="default">{t('common.no', 'Không')}</Tag>;
}

// PasswordPolicyPage — D7 compliance snapshot for QĐ 742 §1.5 password complexity + rotation.
export function PasswordPolicyPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError, dataUpdatedAt } = usePasswordPolicyStatus();

  const title = t('securityHealth.passwordPolicy.title', 'Chính sách mật khẩu');

  return (
    <div>
      <AdminPageHeader
        title={title}
        icon={<SafetyOutlined />}
        description={t(
          'securityHealth.passwordPolicy.qd742Wording',
          'Quy định độ phức tạp mật khẩu (độ dài, ký tự đặc biệt, chữ hoa/thường)',
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
            {/* Section 1: Framework defaults (ASP.NET Identity) */}
            <Divider orientation="left" orientationMargin={0}>
              {t('securityHealth.passwordPolicy.frameworkSection', 'Cấu hình framework (ASP.NET Identity)')}
            </Divider>
            <Descriptions bordered size="small" column={1} labelStyle={{ width: 280, fontWeight: 500 }}>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.frameworkRequiredLength', 'Độ dài tối thiểu (framework)')}>
                <Tag color="blue">{data.frameworkRequiredLength}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.frameworkRequireDigit', 'Yêu cầu chữ số')}>
                <BoolTag value={data.frameworkRequireDigit} />
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.frameworkRequireLowercase', 'Yêu cầu chữ thường')}>
                <BoolTag value={data.frameworkRequireLowercase} />
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.frameworkRequireUppercase', 'Yêu cầu chữ hoa')}>
                <BoolTag value={data.frameworkRequireUppercase} />
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.frameworkRequireNonAlphanumeric', 'Yêu cầu ký tự đặc biệt')}>
                <BoolTag value={data.frameworkRequireNonAlphanumeric} />
              </Descriptions.Item>
            </Descriptions>

            {/* Section 2: Project CredentialPolicy override */}
            <Divider orientation="left" orientationMargin={0} style={{ marginTop: 24 }}>
              {t('securityHealth.passwordPolicy.projectSection', 'Chính sách dự án (CredentialPolicy ghi đè)')}
            </Divider>

            {!data.hasProjectPolicy && (
              <Alert
                type="info"
                showIcon
                style={{ marginBottom: 12 }}
                message={t(
                  'securityHealth.passwordPolicy.noPolicyInfo',
                  'Chưa có CredentialPolicy mặc định. Hệ thống áp dụng cấu hình framework ở trên.',
                )}
              />
            )}

            <Descriptions bordered size="small" column={1} labelStyle={{ width: 280, fontWeight: 500 }}>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.policyName', 'Tên chính sách')}>
                {data.policyName ?? <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.minLength', 'Độ dài tối thiểu')}>
                {data.hasProjectPolicy ? <Tag color="blue">{data.minLength}</Tag> : <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.maxLength', 'Độ dài tối đa')}>
                {data.hasProjectPolicy ? <Tag color="blue">{data.maxLength}</Tag> : <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.requireUppercase', 'Yêu cầu chữ hoa')}>
                {data.hasProjectPolicy ? <BoolTag value={data.requireUppercase} /> : <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.requireLowercase', 'Yêu cầu chữ thường')}>
                {data.hasProjectPolicy ? <BoolTag value={data.requireLowercase} /> : <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.requireDigit', 'Yêu cầu chữ số')}>
                {data.hasProjectPolicy ? <BoolTag value={data.requireDigit} /> : <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.requireSpecialChar', 'Yêu cầu ký tự đặc biệt')}>
                {data.hasProjectPolicy ? <BoolTag value={data.requireSpecialChar} /> : <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.rotationDays', 'Xoay mật khẩu sau (ngày, 0 = không bao giờ)')}>
                {data.hasProjectPolicy ? <Tag color="orange">{data.rotationDays === 0 ? t('securityHealth.passwordPolicy.never', 'Không bao giờ') : data.rotationDays}</Tag> : <Text type="secondary">—</Text>}
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.passwordPolicy.passwordHistoryCount', 'Số mật khẩu cũ bị chặn tái sử dụng')}>
                {data.hasProjectPolicy ? <Tag color="purple">{data.passwordHistoryCount}</Tag> : <Text type="secondary">—</Text>}
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
