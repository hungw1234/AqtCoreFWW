// Custom React Flow node — renders a parallel-branch fork gateway as a diamond shape.
// Mirrors custom-state-node.tsx for handle/position/memo pattern.
// Displayed at the branching point of a WorkflowBranchDto; shows the branch type label.

import { memo } from 'react';
import { Handle, Position } from '@xyflow/react';
import type { NodeProps } from '@xyflow/react';
import { Typography } from 'antd';
import { ApartmentOutlined } from '@ant-design/icons';
import type { GatewayNodeData } from './gateway-node-build-utils';

const { Text } = Typography;

// Diamond side length — equal width/height so the rotated square is symmetric.
// Exported so the dagre auto-layout can use it as the fork's bounding box without re-hardcoding it.
export const DIAMOND_SIZE = 72;

// Handle style — same spec as custom-state-node for visual consistency.
const HANDLE_STYLE = {
  background: '#555',
  width: 9,
  height: 9,
  border: '2px solid #fff',
};

// Gateway color palette — amber conveys "decision / parallel split".
const GW_BG = '#fa8c16';
const GW_BG_SELECTED = '#d46b08';

// CustomGatewayNode — the fork diamond rendered on the visual designer canvas.
// data is cast from the React Flow generic Record type to the typed GatewayNodeData shape.
export const CustomGatewayNode = memo(function CustomGatewayNode({ data, selected }: NodeProps) {
  const gw = data as unknown as GatewayNodeData;
  const bg = selected ? GW_BG_SELECTED : GW_BG;
  const border = selected ? '2px solid #1677ff' : '2px solid #d46b08';
  const boxShadow = selected
    ? '0 0 0 2px rgba(22,119,255,0.3), 0 2px 8px rgba(0,0,0,0.2)'
    : '0 2px 8px rgba(0,0,0,0.2)';

  return (
    <div
      style={{
        width: DIAMOND_SIZE,
        height: DIAMOND_SIZE,
        background: bg,
        border,
        borderRadius: 4,
        transform: 'rotate(45deg)',
        boxShadow,
        cursor: 'default',
        position: 'relative',
      }}
    >
      {/* Handles on all 4 sides — positioned along the rotated diamond perimeter.
          We add both source + target on each side so edges can connect in any direction. */}
      <Handle type="target" position={Position.Top}    id="t-in"  style={HANDLE_STYLE} />
      <Handle type="source" position={Position.Bottom} id="b-out" style={HANDLE_STYLE} />
      <Handle type="source" position={Position.Right}  id="r-out" style={HANDLE_STYLE} />
      <Handle type="target" position={Position.Left}   id="l-in"  style={HANDLE_STYLE} />
      <Handle type="source" position={Position.Top}    id="t-out" style={HANDLE_STYLE} />
      <Handle type="target" position={Position.Right}  id="r-in"  style={HANDLE_STYLE} />
      <Handle type="source" position={Position.Left}   id="l-out" style={HANDLE_STYLE} />
      <Handle type="target" position={Position.Bottom} id="b-in"  style={HANDLE_STYLE} />

      {/* Inner label — counter-rotate so text reads upright inside the tilted diamond. */}
      <div
        style={{
          position: 'absolute',
          inset: 0,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          transform: 'rotate(-45deg)',
          pointerEvents: 'none',
          gap: 2,
        }}
      >
        <ApartmentOutlined style={{ color: '#fff', fontSize: 16 }} />
        <Text
          style={{
            color: '#fff',
            fontSize: 10,
            fontWeight: 700,
            textShadow: '0 1px 2px rgba(0,0,0,0.4)',
            lineHeight: '12px',
          }}
        >
          {gw.branchTypeLabel}
        </Text>
      </div>
    </div>
  );
});
