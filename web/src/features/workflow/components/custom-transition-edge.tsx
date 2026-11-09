// Custom React Flow edge — renders a workflow transition as a smoothstep curve
// Shows: actionLabelVi as edge label, delete button (×) on hover

import { memo, useState } from 'react';
import {
  BaseEdge,
  EdgeLabelRenderer,
  getSmoothStepPath,
  useReactFlow,
} from '@xyflow/react';
import type { EdgeProps } from '@xyflow/react';
import type { WorkflowTransitionDto } from '../workflow-types';

// CustomTransitionEdge — smoothstep edge with action label and hover-delete
export const CustomTransitionEdge = memo(function CustomTransitionEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  data,
  selected,
}: EdgeProps) {
  const [hovered, setHovered] = useState(false);
  const { setEdges } = useReactFlow();
  const transition = data as unknown as WorkflowTransitionDto;

  const [edgePath, labelX, labelY] = getSmoothStepPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });

  const handleDelete = () => {
    setEdges((eds) => eds.filter((e) => e.id !== id));
  };

  const strokeColor = selected ? '#1677ff' : '#888';
  // Direction arrowhead at the target ("to" state) end. Inline per-edge marker so the head color
  // tracks the selected/idle stroke; markerUnits=userSpaceOnUse keeps it a constant size regardless
  // of strokeWidth. orient="auto" rotates the tip to follow the smoothstep path into the target node.
  const markerId = `wf-arrow-${id}`;

  return (
    <>
      <defs>
        <marker
          id={markerId}
          markerWidth={12}
          markerHeight={12}
          refX={9}
          refY={5}
          orient="auto"
          markerUnits="userSpaceOnUse"
        >
          <path d="M0,0 L10,5 L0,10 z" fill={strokeColor} />
        </marker>
      </defs>
      {/* Invisible wider path for easier hover/click detection */}
      <path
        d={edgePath}
        fill="none"
        stroke="transparent"
        strokeWidth={20}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
      />
      <BaseEdge
        id={id}
        path={edgePath}
        markerEnd={`url(#${markerId})`}
        style={{ stroke: strokeColor, strokeWidth: selected ? 2 : 1.5 }}
      />
      <EdgeLabelRenderer>
        <div
          style={{
            position: 'absolute',
            transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)`,
            pointerEvents: 'all',
            display: 'flex',
            alignItems: 'center',
            gap: 4,
          }}
          onMouseEnter={() => setHovered(true)}
          onMouseLeave={() => setHovered(false)}
          className="nodrag nopan"
        >
          {transition?.actionLabelVi && (
            <span
              style={{
                background: selected ? '#e6f4ff' : '#f5f5f5',
                border: `1px solid ${selected ? '#91caff' : '#d9d9d9'}`,
                borderRadius: 4,
                padding: '1px 6px',
                fontSize: 11,
                color: '#333',
                whiteSpace: 'nowrap',
              }}
            >
              {transition.actionLabelVi}
            </span>
          )}
          {(hovered || selected) && (
            <button
              onClick={handleDelete}
              style={{
                background: '#ff4d4f',
                color: '#fff',
                border: 'none',
                borderRadius: '50%',
                width: 16,
                height: 16,
                fontSize: 10,
                lineHeight: '16px',
                cursor: 'pointer',
                padding: 0,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
              title="Xóa hành động"
            >
              ×
            </button>
          )}
        </div>
      </EdgeLabelRenderer>
    </>
  );
});
