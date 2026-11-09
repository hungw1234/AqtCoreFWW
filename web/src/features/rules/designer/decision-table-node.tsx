// Custom React Flow node — renders a DecisionTable as a wider info card
// Shows: table name, input/output column counts, row count
// Distinct light-blue background to differentiate from rule condition nodes

import { memo } from 'react';
import { Handle, Position } from '@xyflow/react';
import type { NodeProps } from '@xyflow/react';
import { Tag, Typography } from 'antd';
import type { DecisionTableDto } from '../rules-api';

const { Text } = Typography;

// DecisionTableNode — displayed in the visual rule flow canvas after condition nodes
export const DecisionTableNode = memo(function DecisionTableNode({ data }: NodeProps) {
  const dt = data as unknown as DecisionTableDto;
  const inputCount = dt.inputColumns?.length ?? 0;
  const outputCount = dt.outputColumns?.length ?? 0;
  const rowCount = dt.rows?.length ?? 0;

  return (
    <div className="decision-table-node">
      <Handle type="target" position={Position.Top} />

      <div className="decision-table-node__header">
        <Text strong ellipsis style={{ maxWidth: 200 }}>
          {dt.name}
        </Text>
        <Tag color="cyan" style={{ marginLeft: 'auto', flexShrink: 0 }}>Table</Tag>
      </div>

      <div className="decision-table-node__meta">
        <Text type="secondary" style={{ fontSize: 11 }}>
          {inputCount} inputs × {outputCount} outputs &nbsp;·&nbsp; {rowCount} rows
        </Text>
      </div>

      <Handle type="source" position={Position.Bottom} />
    </div>
  );
});
