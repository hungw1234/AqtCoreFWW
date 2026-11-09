import { createRoute } from '@tanstack/react-router';
import { Button, Card, Space, Typography } from 'antd';
import { rootRoute } from './root-layout';
import { useAuth } from '@/features/auth';
import { GOV_COLORS } from '@/app/theme';

const { Title, Text } = Typography;

export const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  component: LoginPage,
});

// LoginPage: GOV-branded landing with OIDC redirect trigger
function LoginPage() {
  const { login, isLoading } = useAuth();

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: GOV_COLORS.bgLayout,
      }}
    >
      <Card style={{ width: 400, textAlign: 'center', borderRadius: 12, boxShadow: '0 2px 8px rgba(0,0,0,0.08)' }}>
        <Space direction="vertical" size="large" style={{ width: '100%' }}>
          <div>
            {/* Logo */}
            {import.meta.env.VITE_APP_LOGO_URL ? (
              <img
                src={import.meta.env.VITE_APP_LOGO_URL}
                alt={import.meta.env.VITE_APP_NAME ?? 'Logo'}
                style={{ width: 48, height: 48, borderRadius: 10, objectFit: 'contain', marginBottom: 12 }}
              />
            ) : (
              <div style={{
                width: 48, height: 48, borderRadius: 10,
                background: GOV_COLORS.navy,
                margin: '0 auto 12px',
              }} />
            )}
            <Title level={3} style={{ color: GOV_COLORS.navy, marginBottom: 4 }}>
              {import.meta.env.VITE_APP_NAME ?? 'AqtCoreFW'}
            </Title>
            <Text type="secondary">
              {import.meta.env.VITE_APP_SUBTITLE ?? 'Hệ thống quản trị — Khung CNTT Chính phủ'}
            </Text>
          </div>

          <Button
            type="primary"
            size="large"
            block
            loading={isLoading}
            onClick={() => login()}
          >
            Đăng nhập
          </Button>

          <Text type="secondary" style={{ fontSize: 12 }}>
            © {new Date().getFullYear()} AEQUITAS — Vietnamese Gov Framework
          </Text>
        </Space>
      </Card>
    </div>
  );
}
