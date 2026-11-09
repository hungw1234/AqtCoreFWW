import { Card, Skeleton, Typography, theme } from 'antd';
import { ArrowUpOutlined, ArrowDownOutlined } from '@ant-design/icons';
import { memo } from 'react';

const { Text } = Typography;

interface AdminStatCardProps {
  icon: React.ReactNode;
  title: string;
  value: string | number;
  trend?: { value: number; suffix?: string };
  color?: string;
  loading?: boolean;
}

// AdminStatCard — metric card with icon, value, label, and optional trend indicator
export const AdminStatCard = memo(function AdminStatCard({
  icon, title, value, trend, color, loading,
}: AdminStatCardProps) {
  const { token } = theme.useToken();
  const resolvedColor = color ?? token.colorPrimary;

  if (loading) {
    return (
      <Card variant="borderless" className="gov-card-hover" style={{ boxShadow: 'var(--elevation-1)' }}>
        <Skeleton active paragraph={{ rows: 1 }} title={{ width: '60%' }} />
      </Card>
    );
  }

  const trendColor = trend
    ? trend.value > 0 ? token.colorSuccess : trend.value < 0 ? token.colorError : token.colorTextSecondary
    : undefined;
  const TrendIcon = trend && trend.value !== 0
    ? (trend.value > 0 ? ArrowUpOutlined : ArrowDownOutlined)
    : null;

  return (
    <Card variant="borderless" className="gov-card-hover" style={{ boxShadow: 'var(--elevation-1)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
        {/* Icon circle with tinted background */}
        <div
          style={{
            width: 44,
            height: 44,
            borderRadius: 'var(--radius-md)',
            background: `color-mix(in srgb, ${resolvedColor} 8%, transparent)`,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 20,
            color: resolvedColor,
            flexShrink: 0,
          }}
        >
          {icon}
        </div>

        {/* Value + label + trend */}
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: 24, fontWeight: 700, lineHeight: 1.2, color: 'var(--gov-text-primary)' }}>
            {value}
          </div>
          <Text type="secondary" style={{ fontSize: 13 }}>{title}</Text>
          {trend && (
            <span style={{ marginLeft: 8, fontSize: 12, color: trendColor, fontWeight: 500 }}>
              {TrendIcon && <TrendIcon style={{ fontSize: 10, marginRight: 2 }} />}
              {trend.value === 0 ? '0' : Math.abs(trend.value)}{trend.suffix ?? ''}
            </span>
          )}
        </div>
      </div>
    </Card>
  );
});
