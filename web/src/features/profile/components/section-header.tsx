import { Typography } from 'antd';
import type { ReactNode } from 'react';

const { Title, Text } = Typography;

interface SectionHeaderProps {
  title: string;
  description?: string;
  /** Trailing action (e.g. an "Add" button) aligned to the right of the title row. */
  extra?: ReactNode;
}

/**
 * Title + optional description + right-aligned action, used to head each card section on the
 * profile page. Extracted to remove the repeated flex-header markup in the signatures tab (DRY).
 */
export function SectionHeader({ title, description, extra }: SectionHeaderProps) {
  return (
    <div
      style={{
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'flex-start',
        gap: 12,
        flexWrap: 'wrap',
      }}
    >
      <div style={{ minWidth: 0 }}>
        <Title level={5} style={{ margin: 0 }}>{title}</Title>
        {description && (
          <Text type="secondary" style={{ fontSize: 13 }}>{description}</Text>
        )}
      </div>
      {extra && <div style={{ flexShrink: 0 }}>{extra}</div>}
    </div>
  );
}
