import { Card } from 'antd';

interface AdminContentCardProps {
  children: React.ReactNode;
  noPadding?: boolean; // set true for tables — table has its own internal padding
  className?: string;
}

// AdminContentCard — consistent card wrapper for page content (tables, forms, custom sections).
// Applies 12px radius + elevation-1 shadow matching the design system.
// Use noPadding for table content so the table header aligns flush with card edges.
export function AdminContentCard({ children, noPadding, className }: AdminContentCardProps) {
  return (
    <Card
      variant="borderless"
      className={className}
      style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)' }}
      styles={{ body: { padding: noPadding ? 0 : 24 } }}
    >
      {children}
    </Card>
  );
}
