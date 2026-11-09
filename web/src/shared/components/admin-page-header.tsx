import { Card, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

const { Title, Text } = Typography;

interface AdminPageHeaderProps {
  title: string;
  description?: string;
  icon?: React.ReactNode;
  actions?: React.ReactNode;
  stats?: { total?: number; filtered?: number; label?: string };
}

// AdminPageHeader — consistent page header card with title, actions, and optional stats bar.
// Use at the top of every admin page before the content card.
// Bottom margin uses --page-gap (24px) to match the design rhythm defined in Phase 1.
export function AdminPageHeader({ title, description, icon, actions, stats }: AdminPageHeaderProps) {
  const { t } = useTranslation();
  const hasStats = stats && (stats.total !== undefined || stats.filtered !== undefined);
  const label = stats?.label ?? t('common.items', 'bản ghi');

  return (
    <Card
      variant="borderless"
      style={{
        boxShadow: 'var(--elevation-1)',
        borderRadius: 12,
        marginBottom: 'var(--page-gap)',
      }}
      styles={{ body: { padding: '16px 24px' } }}
    >
      {/* Top row: icon + title on left, action buttons on right */}
      <Flex justify="space-between" align="start" wrap gap={12}>
        <Flex align="center" gap={10} style={{ minWidth: 0, flex: 1 }}>
          {icon && (
            <span
              style={{
                fontSize: 20,
                color: 'var(--gov-navy)',
                flexShrink: 0,
                lineHeight: 1,
              }}
            >
              {icon}
            </span>
          )}
          <Title level={4} style={{ margin: 0, lineHeight: 1.3 }}>
            {title}
          </Title>
        </Flex>

        {actions && (
          <Flex gap={8} wrap align="center" style={{ flexShrink: 0 }}>
            {actions}
          </Flex>
        )}
      </Flex>

      {/* Description line */}
      {description && (
        <Text
          type="secondary"
          style={{ display: 'block', fontSize: 13, marginTop: 6 }}
        >
          {description}
        </Text>
      )}

      {/* Stats bar — total + filtered count */}
      {hasStats && (
        <Text
          style={{
            display: 'block',
            fontSize: 12,
            color: 'var(--gov-text-muted)',
            marginTop: 8,
          }}
        >
          {stats.total !== undefined && `${t('common.total', 'Tổng số')}: ${stats.total} ${label}`}
          {stats.total !== undefined && stats.filtered !== undefined && ' · '}
          {stats.filtered !== undefined && `${t('common.filtered', 'Đã lọc')}: ${stats.filtered}`}
        </Text>
      )}
    </Card>
  );
}
