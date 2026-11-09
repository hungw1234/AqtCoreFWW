// WAI-ARIA grid row renderer — extracted from instance-monitoring-dashboard to keep dashboard
// under 250 LOC budget. Renders a single virtual data row with full ARIA grid cell semantics.
// Each row: role="row" + aria-rowindex (1-based, +2 for header offset) + 7 role="gridcell" children.

import { Checkbox, Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import type { WorkflowInstanceListItem } from './workflow-types';
import { GRID_COLS, ROW_HEIGHT } from './workflow-grid-constants';

/** Derive status label from instance: completed if completedAt set, else active */
function instanceStatus(r: WorkflowInstanceListItem): 'Active' | 'Completed' {
  return r.completedAt ? 'Completed' : 'Active';
}

interface WorkflowGridRowProps {
  /** Instance data for this row — uses thin list projection (T5-16) */
  instance: WorkflowInstanceListItem;
  /** 0-based row index within the instances array */
  rowIndex: number;
  /** Virtual row top offset in px (position:absolute) */
  virtualStart: number;
  /** Ref callback for TanStack Virtual measureElement */
  measureRef: (el: Element | null) => void;
  /** Whether this row is currently selected */
  isSelected: boolean;
  /** Called when the row checkbox onChange fires */
  onCheckboxChange: (checked: boolean, id: string) => void;
  /** Returns tabIndex for roving tabIndex — 0 for focused cell, -1 for all others */
  getCellTabIndex: (row: number, col: number) => 0 | -1;
}

export function WorkflowGridRow({
  instance,
  rowIndex,
  virtualStart,
  measureRef,
  isSelected,
  onCheckboxChange,
  getCellTabIndex,
}: WorkflowGridRowProps) {
  const { t } = useTranslation();
  const isCompleted = !!instance.completedAt;
  const status = instanceStatus(instance);
  // aria-rowindex is 1-based; +2 accounts for the header row at index 1
  const ariaRowIndex = rowIndex + 2;

  return (
    <div
      role="row"
      aria-rowindex={ariaRowIndex}
      aria-selected={isSelected}
      aria-label={t('workflow.instance.rowAriaLabel', { entityId: instance.entityId })}
      data-index={rowIndex}
      ref={measureRef}
      style={{
        position: 'absolute',
        top: virtualStart,
        left: 0,
        right: 0,
        height: ROW_HEIGHT,
        display: 'grid',
        gridTemplateColumns: GRID_COLS,
        alignItems: 'center',
        borderBottom: '1px solid #f0f0f0',
        background: isSelected ? '#e6f4ff' : undefined,
        fontSize: 12,
      }}
    >
      {/* Col 1 — Checkbox cell */}
      <div
        role="gridcell"
        aria-colindex={1}
        tabIndex={getCellTabIndex(rowIndex, 0)}
        data-row={rowIndex}
        data-col={0}
        style={{ paddingLeft: 8 }}
      >
        <Checkbox
          disabled={isCompleted}
          checked={isSelected}
          aria-label={t('workflow.instance.selectRow', { id: instance.entityId })}
          onChange={(e) => onCheckboxChange(e.target.checked, instance.id)}
        />
      </div>

      {/* Col 2 — Entity Type */}
      <div
        role="gridcell"
        aria-colindex={2}
        tabIndex={getCellTabIndex(rowIndex, 1)}
        data-row={rowIndex}
        data-col={1}
        style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: 8 }}
      >
        {instance.entityType}
      </div>

      {/* Col 3 — Entity ID */}
      <div
        role="gridcell"
        aria-colindex={3}
        tabIndex={getCellTabIndex(rowIndex, 2)}
        data-row={rowIndex}
        data-col={2}
        style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: 8 }}
      >
        {instance.entityId}
      </div>

      {/* Col 4 — Current State */}
      <div
        role="gridcell"
        aria-colindex={4}
        tabIndex={getCellTabIndex(rowIndex, 3)}
        data-row={rowIndex}
        data-col={3}
        style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', paddingRight: 8 }}
      >
        {instance.currentStateId}
      </div>

      {/* Col 5 — Started */}
      <div
        role="gridcell"
        aria-colindex={5}
        tabIndex={getCellTabIndex(rowIndex, 4)}
        data-row={rowIndex}
        data-col={4}
      >
        {dayjs(instance.startedAt).format('DD/MM/YYYY')}
      </div>

      {/* Col 6 — Due */}
      <div
        role="gridcell"
        aria-colindex={6}
        tabIndex={getCellTabIndex(rowIndex, 5)}
        data-row={rowIndex}
        data-col={5}
      >
        {instance.dueAt ? dayjs(instance.dueAt).format('DD/MM/YYYY') : '—'}
      </div>

      {/* Col 7 — Status */}
      <div
        role="gridcell"
        aria-colindex={7}
        tabIndex={getCellTabIndex(rowIndex, 6)}
        data-row={rowIndex}
        data-col={6}
      >
        <Tag color={status === 'Active' ? 'blue' : 'green'}>{status}</Tag>
      </div>
    </div>
  );
}
