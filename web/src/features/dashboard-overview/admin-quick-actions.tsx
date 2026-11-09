import { Button, Card, Col, Row } from 'antd';
import {
  UserAddOutlined,
  ScheduleOutlined,
  HeartOutlined,
  SettingOutlined,
} from '@ant-design/icons';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';

// AdminQuickActions — 4 shortcut buttons for common admin tasks
export function AdminQuickActions() {
  const navigate = useNavigate();
  const { t } = useTranslation();

  const actions = [
    {
      key: 'create-user',
      label: t('page.adminDashboard.createUser'),
      icon: <UserAddOutlined />,
      path: '/admin/users',
    },
    {
      key: 'view-jobs',
      label: t('page.adminDashboard.viewJobs'),
      icon: <ScheduleOutlined />,
      path: '/admin/jobs',
    },
    {
      key: 'health-check',
      label: t('page.adminDashboard.healthCheck'),
      icon: <HeartOutlined />,
      path: '/admin/health',
    },
    {
      key: 'system-params',
      label: t('page.adminDashboard.systemParams'),
      icon: <SettingOutlined />,
      path: '/admin/system-params',
    },
  ];

  return (
    <Card
      title={t('page.adminDashboard.quickActions')}
      variant="borderless"
      style={{ boxShadow: 'var(--elevation-1)' }}
    >
      <Row gutter={[12, 12]}>
        {actions.map((action) => (
          <Col key={action.key} xs={12} sm={6}>
            <Button
              block
              icon={action.icon}
              onClick={() => navigate({ to: action.path })}
              style={{ height: 48, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 6 }}
            >
              {action.label}
            </Button>
          </Col>
        ))}
      </Row>
    </Card>
  );
}
