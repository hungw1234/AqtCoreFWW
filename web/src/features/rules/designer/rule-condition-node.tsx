// Custom React Flow node — renders a single Rule as a condition card
// Shows: name (bold), priority badge, enabled status, expression preview

import { memo } from 'react';
import { Handle, Position } from '@xyflow/react';
import type { NodeProps } from '@xyflow/react';
import { Tag, Typography } from 'antd';
import type { RuleDto } from '../rules-api';

const { Text } = Typography;

// RuleConditionNode — displayed in the visual rule flow canvas
export const RuleConditionNode = memo(function RuleConditionNode({ data }: NodeProps) {
  const rule = data as unknown as RuleDto;

  return (
    <div className={`rule-condition-node${rule.enabled === false ? ' rule-condition-node--disabled' : ''}`}>
      <Handle type="target" position={Position.Top} />

      <div className="rule-condition-node__header">
        <Text
          strong
          ellipsis
          style={{
            maxWidth: 180,
            textDecoration: rule.enabled === false ? 'line-through' : undefined,
            color: rule.enabled === false ? '#999' : undefined,
          }}
        >
          {rule.name}
        </Text>
        <Tag color="blue" style={{ marginLeft: 'auto', flexShrink: 0 }}>
          P{rule.priority}
        </Tag>
      </div>

      <div className="rule-condition-node__expression">
        <Text
          type="secondary"
          ellipsis
          style={{ fontFamily: 'monospace', fontSize: 11 }}
        >
          {rule.expression || '(no expression)'}
        </Text>
      </div>

      {rule.enabled === false && (
        <div className="rule-condition-node__badge">
          <Tag color="default" style={{ fontSize: 10 }}>disabled</Tag>
        </div>
      )}

      <Handle type="source" position={Position.Bottom} />
    </div>
  );
});
