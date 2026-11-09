// audit-retention-page.tsx — QĐ 742 §3.2.a/3.2.b D7 visibility: audit retention policies + live row counts.
import { Alert, Descriptions, Spin, Table, Tag, Typography } from 'antd';
import { FileProtectOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useAuditRetentionStatus } from './security-health-api';
import type { RetentionPolicyLineDto } from './security-health-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

const { Text } = Typography;

// AuditRetentionPage — D7 compliance snapshot for QĐ 742 §3.2 retention policy.
export function AuditRetentionPage() {
  const { t } = useTranslation();
  const { data, isLoading, isError, dataUpdatedAt } = useAuditRetentionStatus();

  const columns: ColumnsType<RetentionPolicyLineDto> = [
    {
      title: t('securityHealth.auditRetention.col.categoryId', 'Danh mục'),
      dataIndex: 'categoryId',
      key: 'categoryId',
      width: 120,
      render: (v: number | null) =>
        v == null
          ? <Tag color="blue">{t('securityHealth.auditRetention.globalDefault', 'Mặc định toàn cục')}</Tag>
          : <Tag>{v}</Tag>,
    },
    {
      title: t('securityHealth.auditRetention.col.tenantId', 'Tenant'),
      dataIndex: 'tenantId',
      key: 'tenantId',
      width: 120,
      render: (v: string | null) =>
        v == null
          ? <Text type="secondary">{t('securityHealth.auditRetention.global', 'Toàn cục')}</Text>
          : <Text code style={{ fontSize: 11 }}>{v.slice(0, 8)}…</Text>,
    },
    {
      title: t('securityHealth.auditRetention.col.retentionDays', 'Lưu trữ (ngày)'),
      dataIndex: 'retentionDays',
      key: 'retentionDays',
      width: 140,
      render: (v: number) => <Tag color="orange">{v}</Tag>,
    },
    {
      title: t('securityHealth.auditRetention.col.archiveBeforePurge', 'Lưu trữ trước khi xoá'),
      dataIndex: 'archiveBeforePurge',
      key: 'archiveBeforePurge',
      width: 180,
      render: (v: boolean) =>
        v
          ? <Tag color="success">{t('common.yes', 'Có')}</Tag>
          : <Tag color="default">{t('common.no', 'Không')}</Tag>,
    },
    {
      title: t('securityHealth.auditRetention.col.archivePath', 'Đường dẫn lưu trữ'),
      dataIndex: 'archivePath',
      key: 'archivePath',
      ellipsis: true,
      render: (v: string | null) => v ?? <Text type="secondary">—</Text>,
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('securityHealth.auditRetention.title', 'Chính sách lưu trữ nhật ký')}
        icon={<FileProtectOutlined />}
        description={t(
          'securityHealth.auditRetention.qd742Wording',
          'Lưu trữ nhật ký theo chính sách (thời hạn lưu, sao lưu, loại bỏ)',
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
            {/* Summary row counts */}
            <Descriptions bordered size="small" column={2} style={{ marginBottom: 16 }}>
              <Descriptions.Item label={t('securityHealth.auditRetention.hotRowCount', 'Bản ghi hiện tại (hot)')}>
                <Tag color="blue">{data.hotRowCount.toLocaleString()}</Tag>
              </Descriptions.Item>
              <Descriptions.Item label={t('securityHealth.auditRetention.archiveRowCount', 'Bản ghi lưu trữ (archive)')}>
                {data.archiveRowCount != null
                  ? <Tag color="purple">{data.archiveRowCount.toLocaleString()}</Tag>
                  : <Text type="secondary">{t('securityHealth.notProvisioned', 'Chưa triển khai')}</Text>}
              </Descriptions.Item>
            </Descriptions>

            {/* Policy table */}
            <Table<RetentionPolicyLineDto>
              rowKey="id"
              columns={columns}
              dataSource={data.policies}
              size="small"
              pagination={false}
              locale={{ emptyText: t('common.noData', 'Không có dữ liệu') }}
              scroll={{ x: 700 }}
            />
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
