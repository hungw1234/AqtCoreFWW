import { Avatar, Card, Space, Tag, Typography, theme } from 'antd';
import { MailOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { getInitials } from '@/shared/utils';

const { Title, Text } = Typography;

interface ProfileHeroProps {
  name?: string;
  email?: string;
  roles: string[];
}

/**
 * Page-level identity header shown above the profile tabs. Rendered once so the user's
 * identity persists across both the "Thông tin" and "Chữ ký số" tabs (avatar + name + email +
 * role tags). Data-forward detail still lives in the info tab's account card; this is the
 * glanceable summary. Sources come from OIDC claims via the parent.
 */
export function ProfileHero({ name, email, roles }: ProfileHeroProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const displayName = name?.trim() || '—';

  return (
    <Card variant="borderless" style={{ boxShadow: 'var(--elevation-1)' }} styles={{ body: { padding: 24 } }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 20, flexWrap: 'wrap' }}>
        <Avatar
          size={72}
          style={{
            flexShrink: 0,
            fontSize: 28,
            fontWeight: 600,
            color: token.colorPrimary,
            background: `color-mix(in srgb, ${token.colorPrimary} 12%, transparent)`,
          }}
        >
          {getInitials(displayName)}
        </Avatar>

        <div style={{ minWidth: 0, flex: 1 }}>
          <Title level={3} style={{ margin: 0, lineHeight: 1.25 }}>
            {displayName}
          </Title>
          {email && (
            <Space size={6} style={{ marginTop: 4, color: token.colorTextSecondary }}>
              <MailOutlined style={{ fontSize: 13 }} />
              <Text type="secondary" copyable={{ text: email }}>{email}</Text>
            </Space>
          )}
          <div style={{ marginTop: 10, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {roles.length > 0 ? (
              roles.map((r) => (
                <Tag key={r} color="blue" style={{ margin: 0 }}>{r}</Tag>
              ))
            ) : (
              <Text type="secondary">{t('profile.noRoles')}</Text>
            )}
          </div>
        </div>
      </div>
    </Card>
  );
}
