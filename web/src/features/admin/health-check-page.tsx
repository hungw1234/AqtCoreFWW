// Admin health check page — shows status of all backend services

import { Tag, Button, Space, Typography, Spin, Descriptions } from 'antd';
import { ReloadOutlined, CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { useHealthCheck } from './admin-api';

const { Text } = Typography;

export function HealthCheckPage() {
  const { data: services, isLoading, isFetching, recheck } = useHealthCheck();

  const allHealthy = services?.every((s) => s.status === 'healthy') ?? false;

  const overallTag = isLoading
    ? <Spin size="small" />
    : allHealthy
      ? <Tag color="green" icon={<CheckCircleOutlined />}>Tất cả hoạt động</Tag>
      : <Tag color="red" icon={<CloseCircleOutlined />}>Có lỗi</Tag>;

  return (
    <div>
      <AdminPageHeader
        title="Trạng thái hệ thống"
        actions={
          <Button icon={<ReloadOutlined />} onClick={recheck} loading={isFetching}>
            Kiểm tra lại
          </Button>
        }
      />
      <AdminContentCard>
        <Descriptions column={1} bordered title={overallTag}>
          {isLoading ? (
            <Descriptions.Item label="Loading"><Spin size="small" /></Descriptions.Item>
          ) : (
            services?.map((svc) => (
              <Descriptions.Item key={svc.name} label={svc.name}>
                <Space>
                  <Tag color={svc.status === 'healthy' ? 'green' : 'red'}>
                    {svc.status === 'healthy' ? 'Healthy' : 'Unhealthy'}
                  </Tag>
                  <Text type="secondary">{svc.responseTime}ms</Text>
                </Space>
              </Descriptions.Item>
            ))
          )}
        </Descriptions>
      </AdminContentCard>
    </div>
  );
}
