// Custom React Flow node — renders a single WorkflowState as a rounded card
// Shows: displayNameVi, initial/final badges, configurable background color
// 4 connection sides (top/right/bottom/left), each side stacks a target + source handle
// so user can draw transitions in any orientation (horizontal/vertical/diagonal flows).

import { memo, useState } from 'react';
import { Handle, Position } from '@xyflow/react';
import type { NodeProps } from '@xyflow/react';
import { Tag, Tooltip, Typography } from 'antd';
import { ExclamationCircleFilled, ForkOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { StateNodeData } from './react-flow-canvas-helpers';

const { Text } = Typography;

// AntD error red — matches the "Cuối" final-state tag + the save-fail toast tone.
const ERROR_RED = '#ff4d4f';

// Cap tooltip lines so a many-violation state stays readable; overflow shown as "+N".
const MAX_TOOLTIP_LINES = 3;

// Compact per-state violation summary for the hover tooltip. Bridges the save-fail toast detail
// onto the canvas so the user can read WHY a state is flagged without re-reading the toast.
function ViolationTooltipBody({ violations }: { violations: NonNullable<StateNodeData['violations']> }) {
  const shown = violations.slice(0, MAX_TOOLTIP_LINES);
  const overflow = violations.length - shown.length;
  return (
    <div style={{ maxWidth: 240 }}>
      {shown.map((v, i) => (
        <div key={i} style={{ fontSize: 12, lineHeight: '18px' }}>
          {`${v.rule}: ${v.detail ?? v.rule}`}
        </div>
      ))}
      {overflow > 0 && (
        <div style={{ fontSize: 12, lineHeight: '18px', opacity: 0.85 }}>{`+${overflow}`}</div>
      )}
    </div>
  );
}

// Darken a hex color for border usage
function darkenHex(hex: string): string {
  const n = parseInt(hex.replace('#', ''), 16);
  const r = Math.max(0, ((n >> 16) & 0xff) - 40);
  const g = Math.max(0, ((n >> 8) & 0xff) - 40);
  const b = Math.max(0, (n & 0xff) - 40);
  return `#${r.toString(16).padStart(2, '0')}${g.toString(16).padStart(2, '0')}${b.toString(16).padStart(2, '0')}`;
}

// Handle style shared across all 8 anchors — source + target stacked per side
const HANDLE_STYLE = {
  background: '#555',
  width: 9,
  height: 9,
  border: '2px solid #fff',
};

// CustomStateNode — displayed in the visual workflow designer canvas
export const CustomStateNode = memo(function CustomStateNode({ data, selected }: NodeProps) {
  const { t } = useTranslation();
  // Local hover state drives the fork-button reveal — kept in the node (NOT in `data`) so hovering
  // never triggers a rebuild/re-sync of the whole node array.
  const [hovered, setHovered] = useState(false);
  const state = data as unknown as StateNodeData;
  const bg = state.color ?? '#808080';
  const hasViolation = state.hasViolation === true;
  // Fork affordance: shown only on forkable states (non-final, not already forked) while the node
  // is selected or hovered. canFork + onAddFork are injected by buildNodes (read-only canvas omits them).
  const showForkButton = state.canFork === true && (selected || hovered) && !!state.onAddFork;

  // Border priority: selection wins over error wins over default (selecting an error node should
  // show the normal selection affordance). The error CUE is carried instead by a glow + corner
  // badge that both survive selection — so the user never loses sight of a flagged state.
  const border = selected
    ? '2px solid #1677ff'
    : hasViolation
      ? `2px solid ${ERROR_RED}`
      : `2px solid ${darkenHex(bg)}`;

  // Layered shadow: error glow ring sits UNDER the selection halo. Stacking (not replacing) keeps
  // the cue legible against any state.color, including reddish ones (the ring offset separates it).
  const errorGlow = '0 0 0 3px rgba(255,77,79,0.45)';
  const boxShadow = selected
    ? `0 0 0 2px rgba(22,119,255,0.3)${hasViolation ? `, ${errorGlow}` : ''}`
    : hasViolation
      ? `${errorGlow}, 0 2px 6px rgba(0,0,0,0.15)`
      : '0 2px 6px rgba(0,0,0,0.15)';

  const card = (
    <div
      aria-invalid={hasViolation || undefined}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        position: 'relative',
        background: bg,
        border,
        borderRadius: 10,
        padding: '8px 14px',
        minWidth: 140,
        maxWidth: 200,
        boxShadow,
        cursor: 'default',
      }}
    >
      {showForkButton && (
        // Top-left fork button — mirrors the top-right violation badge placement. stopPropagation on
        // mousedown+click so the click authors a fork WITHOUT also selecting/dragging the node.
        <Tooltip title={t('workflow.designer.addForkNode', 'Thêm nhánh song song từ bước này')}>
          <span
            role="button"
            aria-label={t('workflow.designer.addForkNode', 'Thêm nhánh song song từ bước này')}
            // nodrag/nopan: React Flow attaches native d3-drag on .react-flow__node ABOVE this span,
            // so synthetic stopPropagation alone can't suppress it — these classes are the canonical
            // escape hatch (same as custom-transition-edge.tsx). stopPropagation kept as belt-and-braces.
            className="nodrag nopan"
            onMouseDown={(e) => e.stopPropagation()}
            onClick={(e) => {
              e.stopPropagation();
              state.onAddFork?.(state.id);
            }}
            style={{
              position: 'absolute',
              top: -7,
              left: -7,
              width: 20,
              height: 20,
              borderRadius: '50%',
              background: '#1677ff',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              boxShadow: '0 1px 3px rgba(0,0,0,0.3)',
              cursor: 'pointer',
              zIndex: 2,
            }}
          >
            <ForkOutlined style={{ color: '#fff', fontSize: 12 }} />
          </span>
        </Tooltip>
      )}
      {hasViolation && (
        // Free-floating corner badge — absolutely positioned so it never displaces card layout,
        // and persists even when the node is selected (selection changes the border, not the badge).
        // A dedicated badge (vs a second red Tag) avoids ambiguity with the red "Cuối" final-state tag,
        // and shows even on states that render no tag row at all.
        <span
          role="img"
          aria-label="Lỗi xác thực"
          style={{
            position: 'absolute',
            top: -7,
            right: -7,
            width: 18,
            height: 18,
            borderRadius: '50%',
            background: '#fff',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            boxShadow: '0 1px 3px rgba(0,0,0,0.3)',
            zIndex: 1,
          }}
        >
          <ExclamationCircleFilled style={{ color: ERROR_RED, fontSize: 16 }} />
        </span>
      )}
      {/* Declaration order matters: ReactFlow picks the FIRST source-typed + FIRST target-typed
         handle for edges without explicit sourceHandle/targetHandle. Declare top-target +
         bottom-source FIRST to preserve default top-down routing. Remaining handles enable
         user-initiated reconnect to other sides. */}
      <Handle type="target" position={Position.Top} id="t-in" style={HANDLE_STYLE} />
      <Handle type="source" position={Position.Bottom} id="b-out" style={HANDLE_STYLE} />

      {/* Secondary handles — used only when user explicitly reconnects to a side */}
      <Handle type="source" position={Position.Top} id="t-out" style={HANDLE_STYLE} />
      <Handle type="target" position={Position.Right} id="r-in" style={HANDLE_STYLE} />
      <Handle type="source" position={Position.Right} id="r-out" style={HANDLE_STYLE} />
      <Handle type="target" position={Position.Left} id="l-in" style={HANDLE_STYLE} />
      <Handle type="source" position={Position.Left} id="l-out" style={HANDLE_STYLE} />

      <div style={{ display: 'flex', alignItems: 'center', gap: 4, flexWrap: 'wrap' }}>
        <Text
          strong
          ellipsis
          style={{ color: '#fff', textShadow: '0 1px 2px rgba(0,0,0,0.4)', maxWidth: 150, flex: 1 }}
          title={state.displayNameVi}
        >
          {state.displayNameVi}
        </Text>
      </div>

      <Text
        type="secondary"
        style={{ fontSize: 10, color: 'rgba(255,255,255,0.75)', display: 'block' }}
      >
        {state.name}
      </Text>

      {(state.isInitial || state.isFinal) && (
        <div style={{ marginTop: 4, display: 'flex', gap: 4 }}>
          {state.isInitial && (
            <Tag color="green" style={{ fontSize: 10, lineHeight: '16px', padding: '0 4px', margin: 0 }}>
              Đầu
            </Tag>
          )}
          {state.isFinal && (
            <Tag color="red" style={{ fontSize: 10, lineHeight: '16px', padding: '0 4px', margin: 0 }}>
              Cuối
            </Tag>
          )}
        </div>
      )}

      <Handle type="target" position={Position.Bottom} id="b-in" style={HANDLE_STYLE} />
    </div>
  );

  // Wrap in a tooltip only when violations exist — surfaces the per-state rule detail on hover.
  // mouseEnterDelay avoids flicker while the user pans across nodes.
  if (hasViolation && state.violations && state.violations.length > 0) {
    return (
      <Tooltip
        placement="top"
        mouseEnterDelay={0.3}
        title={<ViolationTooltipBody violations={state.violations} />}
      >
        {card}
      </Tooltip>
    );
  }

  return card;
});
