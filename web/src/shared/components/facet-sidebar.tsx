import { Card, Checkbox, Space, Typography } from 'antd';
import type { CheckboxChangeEvent } from 'antd/es/checkbox';

const { Text } = Typography;

export interface FacetBucket {
  value: string;
  label: string;
  count: number;
}

export interface FacetGroup {
  key: string;
  label: string;
  buckets: FacetBucket[];
}

interface FacetSidebarProps {
  facets: FacetGroup[];
  /** Map of facet key → set of selected values */
  selected: Record<string, Set<string>>;
  onChange: (facetKey: string, value: string, checked: boolean) => void;
}

// FacetSidebar — renders filter facet groups with checkboxes and doc counts
export function FacetSidebar({ facets, selected, onChange }: FacetSidebarProps) {
  if (!facets.length) return null;

  return (
    <Space direction="vertical" style={{ width: '100%' }} size={8}>
      {facets.map((group) => (
        <Card
          key={group.key}
          title={group.label}
          size="small"
          styles={{ body: { padding: '8px 12px' } }}
        >
          <Space direction="vertical" size={4} style={{ width: '100%' }}>
            {group.buckets.map((bucket) => (
              <Checkbox
                key={bucket.value}
                checked={selected[group.key]?.has(bucket.value) ?? false}
                onChange={(e: CheckboxChangeEvent) =>
                  onChange(group.key, bucket.value, e.target.checked)
                }
              >
                <Text style={{ fontSize: 13 }}>{bucket.label}</Text>
                <Text type="secondary" style={{ fontSize: 12, marginLeft: 4 }}>
                  ({bucket.count})
                </Text>
              </Checkbox>
            ))}
          </Space>
        </Card>
      ))}
    </Space>
  );
}
