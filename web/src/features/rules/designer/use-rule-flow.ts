// Converts RuleDto[] + DecisionTableDto[] into @xyflow/react nodes and edges
// Layout: vertical stack sorted by priority (top → bottom)

import { useMemo } from 'react';
import type { Node, Edge } from '@xyflow/react';
import type { RuleDto, DecisionTableDto } from '../rules-api';
import {
  NODE_WIDTH,
  NODE_HEIGHT,
  TABLE_NODE_HEIGHT,
  VERTICAL_GAP,
  NODE_TYPE_RULE_CONDITION,
  NODE_TYPE_DECISION_TABLE,
} from './flow-constants';

export interface RuleFlowData {
  nodes: Node[];
  edges: Edge[];
}

const START_NODE_ID = '__start__';
const END_NODE_ID = '__end__';
const X_CENTER = 0;

// Build nodes and edges for the flow canvas from API data
export function useRuleFlow(rules: RuleDto[], decisionTables: DecisionTableDto[]): RuleFlowData {
  return useMemo(() => {
    const sorted = [...rules].sort((a, b) => a.priority - b.priority);
    const nodes: Node[] = [];
    const edges: Edge[] = [];

    let y = 0;

    // Start node
    nodes.push({
      id: START_NODE_ID,
      type: 'input',
      position: { x: X_CENTER, y },
      data: { label: 'Start Evaluation' },
      style: { width: NODE_WIDTH, background: '#52c41a', color: '#fff', border: 'none', borderRadius: 8 },
    });
    y += NODE_HEIGHT + VERTICAL_GAP;

    let prevId = START_NODE_ID;

    // Rule condition nodes
    for (const rule of sorted) {
      const nodeId = `rule-${rule.id}`;
      nodes.push({
        id: nodeId,
        type: NODE_TYPE_RULE_CONDITION,
        position: { x: X_CENTER, y },
        data: rule as unknown as Record<string, unknown>,
        style: { width: NODE_WIDTH },
      });
      edges.push({
        id: `e-${prevId}-${nodeId}`,
        source: prevId,
        target: nodeId,
        type: 'smoothstep',
        animated: false,
      });
      prevId = nodeId;
      y += NODE_HEIGHT + VERTICAL_GAP;
    }

    // Decision table nodes (appended after rules)
    for (const dt of decisionTables) {
      const nodeId = `dt-${dt.id}`;
      nodes.push({
        id: nodeId,
        type: NODE_TYPE_DECISION_TABLE,
        position: { x: X_CENTER, y },
        data: dt as unknown as Record<string, unknown>,
        style: { width: NODE_WIDTH },
      });
      edges.push({
        id: `e-${prevId}-${nodeId}`,
        source: prevId,
        target: nodeId,
        type: 'smoothstep',
        animated: false,
      });
      prevId = nodeId;
      y += TABLE_NODE_HEIGHT + VERTICAL_GAP;
    }

    // End node
    nodes.push({
      id: END_NODE_ID,
      type: 'output',
      position: { x: X_CENTER, y },
      data: { label: 'End' },
      style: { width: NODE_WIDTH, background: '#d9d9d9', border: 'none', borderRadius: 8 },
    });
    edges.push({
      id: `e-${prevId}-${END_NODE_ID}`,
      source: prevId,
      target: END_NODE_ID,
      type: 'smoothstep',
    });

    return { nodes, edges };
  }, [rules, decisionTables]);
}
