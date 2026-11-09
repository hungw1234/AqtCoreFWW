import { createRoute } from '@tanstack/react-router';
import { Card, Typography, Space, Tag } from 'antd';
import { authenticatedRoute } from './authenticated-layout';

const { Title, Paragraph } = Typography;

// Home page route — child of authenticated layout (requires login)
export const indexRoute = createRoute({
  getParentRoute: () => authenticatedRoute,
  path: '/',
  component: IndexPage,
});

function IndexPage() {
  return (
    <Card style={{ maxWidth: 600, margin: '48px auto' }}>
      <Space direction="vertical" size="middle" style={{ width: '100%' }}>
        <Title level={4} style={{ margin: 0 }}>
          GOV Admin Dashboard
        </Title>
        <Paragraph type="secondary">
          AqtCoreFW — Vietnamese Government .NET Core Framework v1.5
        </Paragraph>
        <Space wrap>
          <Tag color="blue">React 19</Tag>
          <Tag color="green">Ant Design 5</Tag>
          <Tag color="purple">TanStack Router</Tag>
          <Tag color="orange">TypeScript 5.7</Tag>
        </Space>
      </Space>
    </Card>
  );
}
