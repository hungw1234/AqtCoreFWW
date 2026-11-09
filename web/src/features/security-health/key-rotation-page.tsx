// key-rotation-page.tsx — QĐ 742 §4.4.a D7 visibility: encryption key rotation status.
// LastRotationAt/NextScheduledRotationAt are null (backlog: IKeyRotationMetadataReader) — show "—".
import { Alert, Descriptions, Spin, Tag, Typography } from 'antd';
import { KeyOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useKeyRotationStatus } from './security-health-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

// KeyRotationPage — D7 compliance snapshot for QĐ 742 §4.4.a key lifecycle management.
export function KeyRotationPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError, dataUpdatedAt } = useKeyRotationStatus();

  return (
    <div>
      <AdminPageHeader
        title={t('securityHealth.keyRotation.title', 'Vòng đời khoá mã hoá')}
        icon={<KeyOutlined />}
        description={t(
          'securityHealth.keyRotation.qd742Wording',
          'Quản lý vòng đời khoá mã hoá (sinh, lưu, sử dụng, xoay vòng, huỷ)',
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
            {/* Inform auditor that rotation timestamps are backlogged */}
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 16 }}
              message={t(
                'securityHealth.keyRotation.metadataGapInfo',
                'Metadata xoay vòng khoá (thời điểm xoay, lịch kế hoạch) chưa hiển thị — đang chờ tích hợp IKeyRotationMetadataReader (backlog). Phiên bản khoá hiện tại từ cấu hình Vault.',
              )}
            />

            <Descriptions
              bordered
              size="small"
              column={1}
              labelStyle={{ width: 320, fontWeight: 500 }}
            >
              <Descriptions.Item
                label={t('securityHealth.keyRotation.currentKeyVersion', 'Phiên bản khoá hiện tại')}
              >
                <Tag color="blue">v{data.currentKeyVersion}</Tag>
              </Descriptions.Item>
              <Descriptions.Item
                label={t('securityHealth.keyRotation.lastRotationAt', 'Lần xoay vòng gần nhất')}
              >
                {data.lastRotationAt ? (
                  dayjs(data.lastRotationAt).format('DD/MM/YYYY HH:mm:ss')
                ) : (
                  <Text type="secondary">
                    — {t('securityHealth.keyRotation.pendingMetadata', '(chờ tích hợp metadata)')}
                  </Text>
                )}
              </Descriptions.Item>
              <Descriptions.Item
                label={t(
                  'securityHealth.keyRotation.nextScheduledRotationAt',
                  'Lịch xoay vòng tiếp theo',
                )}
              >
                {data.nextScheduledRotationAt ? (
                  <Tag color="orange">
                    {dayjs(data.nextScheduledRotationAt).format('DD/MM/YYYY HH:mm:ss')}
                  </Tag>
                ) : (
                  <Text type="secondary">
                    — {t('securityHealth.keyRotation.pendingMetadata', '(chờ tích hợp metadata)')}
                  </Text>
                )}
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
