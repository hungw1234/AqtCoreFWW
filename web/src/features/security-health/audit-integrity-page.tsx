// audit-integrity-page.tsx — QĐ 742 §3.4.a D7 visibility: audit log immutability trigger health.
import { Alert, Descriptions, Spin, Tag, Typography } from 'antd';
import {
  CheckCircleOutlined,
  CloseCircleOutlined,
  AuditOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useAuditIntegrityStatus } from './security-health-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

// AuditIntegrityPage — D7 compliance snapshot for QĐ 742 §3.4.a immutability trigger health.
export function AuditIntegrityPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError, dataUpdatedAt } = useAuditIntegrityStatus();

  return (
    <div>
      <AdminPageHeader
        title={t('securityHealth.auditIntegrity.title', 'Tính toàn vẹn nhật ký kiểm tra')}
        icon={<AuditOutlined />}
        description={t(
          'securityHealth.auditIntegrity.qd742Wording',
          'Bảo đảm tính toàn vẹn của nhật ký kiểm tra (chống chỉnh sửa, xoá)',
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
            {/* Compliance gap alert when trigger is inactive */}
            {!data.immutabilityTriggerActive && (
              <Alert
                type="error"
                showIcon
                style={{ marginBottom: 16 }}
                message={t(
                  'securityHealth.auditIntegrity.triggerInactiveAlert',
                  'Cảnh báo tuân thủ: trigger bảo vệ nhật ký đang bị tắt hoặc không tồn tại!',
                )}
                description={t(
                  'securityHealth.auditIntegrity.triggerInactiveDetail',
                  'Trigger tr_AuditLog_NoMutate không hoạt động — nhật ký có thể bị sửa hoặc xoá. Khắc phục ngay để đảm bảo QĐ 742 §3.4.a.',
                )}
              />
            )}

            <Descriptions
              bordered
              size="small"
              column={1}
              labelStyle={{ width: 320, fontWeight: 500 }}
            >
              <Descriptions.Item
                label={t(
                  'securityHealth.auditIntegrity.immutabilityTriggerActive',
                  'Trigger bảo vệ nhật ký (không chỉnh sửa / xoá)',
                )}
              >
                {data.immutabilityTriggerActive ? (
                  <Tag icon={<CheckCircleOutlined />} color="success">
                    {t('securityHealth.active', 'Đang hoạt động')}
                  </Tag>
                ) : (
                  <Tag icon={<CloseCircleOutlined />} color="error">
                    {t('securityHealth.inactive', 'Không hoạt động')}
                  </Tag>
                )}
              </Descriptions.Item>
              <Descriptions.Item
                label={t('securityHealth.auditIntegrity.triggerName', 'Tên trigger SQL Server')}
              >
                <Text code>{data.triggerName}</Text>
              </Descriptions.Item>
              <Descriptions.Item
                label={t('securityHealth.auditIntegrity.totalLogEntries', 'Tổng số bản ghi nhật ký')}
              >
                <Tag color="blue">{data.totalLogEntries.toLocaleString()}</Tag>
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
