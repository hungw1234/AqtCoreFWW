// Main Visual Rule Designer canvas component
// Wraps @xyflow/react with drag-drop reorder, double-click edit, and batch save
import '@xyflow/react/dist/style.css';
import './rule-flow-canvas.css';

import { useState, useCallback, useMemo } from 'react';
import { ReactFlow, ReactFlowProvider, Controls, Background, MiniMap, useNodesState } from '@xyflow/react';
import type { NodeMouseHandler, OnNodeDrag } from '@xyflow/react';
import { Button, Typography } from 'antd';
import { SaveOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';

import type { RuleDto, DecisionTableDto } from '../rules-api';
import { useUpdateRule } from '../rules-api';
import { useRuleFlow } from './use-rule-flow';
import { useRuleDesignerState } from './use-rule-designer-state';
import { RuleConditionNode } from './rule-condition-node';
import { DecisionTableNode } from './decision-table-node';
import { RuleEditModal } from './rule-edit-modal';
import { NODE_TYPE_RULE_CONDITION, NODE_TYPE_DECISION_TABLE, NODE_WIDTH } from './flow-constants';
import { appMessage } from '@/core/api/notification-bridge';

const { Text } = Typography;

// nodeTypes must be defined outside render to avoid re-registrations
const NODE_TYPES = {
  [NODE_TYPE_RULE_CONDITION]: RuleConditionNode,
  [NODE_TYPE_DECISION_TABLE]: DecisionTableNode,
};

interface RuleFlowCanvasProps {
  ruleSetId: string;
  rules: RuleDto[];
  decisionTables: DecisionTableDto[];
  isReadOnly: boolean;
}

// Inner canvas — must be inside ReactFlowProvider
function RuleFlowInner({ ruleSetId, rules, decisionTables, isReadOnly }: RuleFlowCanvasProps) {
  const { t } = useTranslation();
  const { nodes: initialNodes, edges } = useRuleFlow(rules, decisionTables);
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [editingRule, setEditingRule] = useState<RuleDto | null>(null);
  const [modalOpen, setModalOpen] = useState(false);
  const designerState = useRuleDesignerState();
  const updateRule = useUpdateRule();

  // Re-sync nodes when rules prop changes (e.g. after save + invalidate)
  const { nodes: freshNodes } = useRuleFlow(rules, decisionTables);
  useMemo(() => { setNodes(freshNodes); }, [freshNodes]); // eslint-disable-line react-hooks/exhaustive-deps

  // Drag stop: recalculate priorities from vertical Y positions
  const handleNodeDragStop: OnNodeDrag = useCallback(
    (_event, _node, allNodes) => {
      if (isReadOnly) return;
      // Filter only rule nodes, sort by Y, reassign priorities 0-based
      const ruleNodes = allNodes
        .filter((n) => n.type === NODE_TYPE_RULE_CONDITION)
        .sort((a, b) => a.position.y - b.position.y);

      ruleNodes.forEach((n, idx) => {
        const rule = n.data as unknown as RuleDto;
        if (rule.priority !== idx) {
          designerState.markPriorityChange(rule.id, idx);
        }
      });

      // Snap nodes back to center X to prevent horizontal drift
      setNodes((nds) =>
        nds.map((n) =>
          n.type === NODE_TYPE_RULE_CONDITION || n.type === NODE_TYPE_DECISION_TABLE
            ? { ...n, position: { ...n.position, x: 0 } }
            : n,
        ),
      );
    },
    [isReadOnly, designerState, setNodes],
  );

  // Lock horizontal drag — only allow vertical reordering
  const handleNodeDrag: OnNodeDrag = useCallback(
    (_event, node, _allNodes) => {
      if (node.type === NODE_TYPE_RULE_CONDITION) {
        setNodes((nds) =>
          nds.map((n) => (n.id === node.id ? { ...n, position: { ...n.position, x: 0 } } : n)),
        );
      }
    },
    [setNodes],
  );

  // Double-click on a rule node opens edit modal
  const handleNodeDoubleClick: NodeMouseHandler = useCallback(
    (_event, node) => {
      if (node.type !== NODE_TYPE_RULE_CONDITION) return;
      const rule = node.data as unknown as RuleDto;
      setEditingRule(rule);
      setModalOpen(true);
    },
    [],
  );

  // Modal save — record field changes in designer state
  function handleModalSave(ruleId: string, changes: object) {
    designerState.markRuleEdit(ruleId, changes);
    setModalOpen(false);
  }

  // Batch save all pending changes sequentially
  async function handleSave() {
    const changes = designerState.getChanges();
    try {
      for (const { ruleId, changes: body } of changes) {
        await updateRule.mutateAsync({ ruleSetId, ruleId, body });
      }
      designerState.resetChanges();
      appMessage.success(t('rules.designer.saved'));
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  const showMiniMap = rules.length + decisionTables.length > 10;

  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      {isReadOnly && (
        <div className="rule-designer-readonly-banner">
          {t('rules.designer.readOnly')}
        </div>
      )}

      <div className="rule-flow-canvas">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={NODE_TYPES}
          onNodesChange={onNodesChange}
          onNodeDrag={!isReadOnly ? handleNodeDrag : undefined}
          onNodeDragStop={!isReadOnly ? handleNodeDragStop : undefined}
          onNodeDoubleClick={handleNodeDoubleClick}
          nodesDraggable={!isReadOnly}
          nodesConnectable={false}
          elementsSelectable={!isReadOnly}
          fitView
          fitViewOptions={{ padding: 0.2 }}
          minZoom={0.3}
          defaultViewport={{ x: NODE_WIDTH / 2, y: 20, zoom: 0.85 }}
        >
          <Controls />
          <Background gap={16} />
          {showMiniMap && <MiniMap nodeStrokeWidth={3} />}
        </ReactFlow>
      </div>

      {!isReadOnly && designerState.isDirty && (
        <div className="rule-designer-save-bar">
          <Text type="warning">
            {t('rules.designer.unsavedChanges', { count: designerState.changeCount })}
          </Text>
          <Button
            type="primary"
            icon={<SaveOutlined />}
            onClick={handleSave}
            loading={updateRule.isPending}
            size="small"
          >
            {t('common.save')}
          </Button>
        </div>
      )}

      <RuleEditModal
        rule={editingRule}
        open={modalOpen}
        isReadOnly={isReadOnly}
        onSave={handleModalSave}
        onCancel={() => setModalOpen(false)}
      />
    </div>
  );
}

// RuleFlowCanvas — exported canvas wrapped in required ReactFlowProvider
export function RuleFlowCanvas(props: RuleFlowCanvasProps) {
  return (
    <ReactFlowProvider>
      <RuleFlowInner {...props} />
    </ReactFlowProvider>
  );
}
