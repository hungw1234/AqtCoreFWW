import { Alert, Button, Col, Row, Space, Typography, theme } from 'antd';
import {
  HeartOutlined,
  TeamOutlined,
  DesktopOutlined,
  ScheduleOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { AdminStatCard } from '@/shared/components/admin-stat-card';
import { useAdminDashboardStats } from '@/features/dashboard-overview/admin-dashboard-api';
import { AdminQuickActions } from '@/features/dashboard-overview/admin-quick-actions';
import { AdminRecentActivity } from '@/features/dashboard-overview/admin-recent-activity';

const { Title } = Typography;

// DashboardPage — system-level overview rendered at "/" (trang chủ).
// Inlines the previous AdminDashboardPage content; the dedicated /admin route
// has been removed so the home page is the single overview surface.
export function DashboardPage() {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const { data, isLoading, isError, refetch } = useAdminDashboardStats();

  const healthColor =
    data?.healthStatus === 'healthy'
      ? token.colorSuccess
      : data?.healthStatus === 'degraded'
      ? token.colorWarning
      : token.colorError;

  const healthValue =
    data?.healthStatus === 'healthy'
      ? 'Healthy'
      : data?.healthStatus === 'degraded'
      ? 'Degraded'
      : 'Unhealthy';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Title level={4} style={{ margin: 0 }}>
          {t('page.adminDashboard.title')}
        </Title>
        <Button
          icon={<ReloadOutlined />}
          onClick={refetch}
          loading={isLoading}
        >
          {t('page.adminDashboard.refresh')}
        </Button>
      </div>

      {isError && (
        <Alert
          type="error"
          message={t('common.error')}
          description={t('common.loading')}
          action={
            <Button size="small" onClick={refetch}>
              {t('common.reload')}
            </Button>
          }
        />
      )}

      <Row gutter={[16, 16]}>
        <Col xs={24} sm={12} lg={6}>
          <AdminStatCard
            icon={<HeartOutlined />}
            title={t('page.adminDashboard.health')}
            value={isLoading ? '—' : healthValue}
            color={healthColor}
            loading={isLoading}
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <AdminStatCard
            icon={<TeamOutlined />}
            title={t('page.adminDashboard.users')}
            value={isLoading ? '—' : (data?.totalUsers ?? 0)}
            color={token.colorInfo}
            loading={isLoading}
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <AdminStatCard
            icon={<DesktopOutlined />}
            title={t('page.adminDashboard.sessions')}
            value={isLoading ? '—' : (data?.activeSessions ?? 0)}
            color={token.colorWarning}
            loading={isLoading}
          />
        </Col>
        <Col xs={24} sm={12} lg={6}>
          <AdminStatCard
            icon={<ScheduleOutlined />}
            title={t('page.adminDashboard.jobs')}
            value={isLoading ? '—' : (data?.pendingJobs ?? 0)}
            color={token.colorPrimary}
            loading={isLoading}
          />
        </Col>
      </Row>

      <Space direction="vertical" style={{ width: '100%' }} size={0}>
        <AdminQuickActions />
      </Space>

      <AdminRecentActivity />
    </div>
  );
}
