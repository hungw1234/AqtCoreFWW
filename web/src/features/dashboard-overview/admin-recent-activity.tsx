import { Card, Empty, Skeleton, Timeline, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useRecentAdminActivity } from './admin-dashboard-api';
import type { AdminActivityEntry } from './admin-dashboard-types';

const { Text } = Typography;

// Format timestamp as relative time (e.g. "2 minutes ago" / "3 phút trước")
function relativeTime(timestamp: string, locale: string): string {
  const diff = Date.now() - new Date(timestamp).getTime();
  const minutes = Math.floor(diff / 60_000);
  const hours = Math.floor(diff / 3_600_000);
  const days = Math.floor(diff / 86_400_000);

  if (locale === 'vi') {
    if (days > 0) return `${days} ngày trước`;
    if (hours > 0) return `${hours} giờ trước`;
    if (minutes > 0) return `${minutes} phút trước`;
    return 'Vừa xong';
  }
  if (days > 0) return `${days}d ago`;
  if (hours > 0) return `${hours}h ago`;
  if (minutes > 0) return `${minutes}m ago`;
  return 'Just now';
}

// Map action to tag color for visual distinction
function actionColor(action: string): string {
  const lower = action.toLowerCase();
  if (lower.includes('create') || lower.includes('add')) return 'green';
  if (lower.includes('delete') || lower.includes('remove')) return 'red';
  if (lower.includes('update') || lower.includes('edit')) return 'blue';
  if (lower.includes('login') || lower.includes('auth')) return 'gold';
  return 'default';
}

function ActivityItem({ entry, locale }: { entry: AdminActivityEntry; locale: string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2, paddingBottom: 4 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <Text strong style={{ fontSize: 13 }}>{entry.userName}</Text>
        <Tag color={actionColor(entry.action)} style={{ fontSize: 11, margin: 0 }}>
          {entry.action}
        </Tag>
        {entry.resourceType && (
          <Tag style={{ fontSize: 11, margin: 0 }}>{entry.resourceType}</Tag>
        )}
      </div>
      {/* Module + resource context line */}
      <Text type="secondary" style={{ fontSize: 12 }}>
        {entry.moduleName}
        {entry.resourceId ? ` · ${entry.resourceId.substring(0, 8)}…` : ''}
        {' · '}
        {relativeTime(entry.occurredAt, locale)}
      </Text>
    </div>
  );
}

// AdminRecentActivity — Timeline of last 10 audit log entries
export function AdminRecentActivity() {
  const { t, i18n } = useTranslation();
  const { data, isLoading } = useRecentAdminActivity();
  const locale = i18n.language;

  return (
    <Card
      title={t('page.adminDashboard.recentActivity')}
      variant="borderless"
      style={{ boxShadow: 'var(--elevation-1)' }}
    >
      {isLoading ? (
        <Skeleton active paragraph={{ rows: 5 }} title={false} />
      ) : !data || data.length === 0 ? (
        <Empty description={t('page.adminDashboard.noActivity')} />
      ) : (
        <Timeline
          items={data.map((entry) => ({
            key: entry.id,
            children: <ActivityItem entry={entry} locale={locale} />,
          }))}
        />
      )}
    </Card>
  );
}
