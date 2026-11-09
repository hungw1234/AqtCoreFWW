// Instance monitoring dashboard — KPI cards + virtual-scroll grid for admins.
// WAI-ARIA 1.2 grid, @tanstack/react-virtual v3, focus persistence after scroll churn.
// T5-16: KPIs now sourced from summary prop (detached from paged window).
// RT-08: select-all scope changed to loaded window (not all-active); tooltip added.

import { useState, useRef, useEffect, useMemo, useCallback } from 'react';
import { Typography, Row, Col, Card, Statistic, Space, Checkbox, Spin, Button, Tooltip } from 'antd';
import { useVirtualizer } from '@tanstack/react-virtual';
import { useTranslation } from 'react-i18next';
import type { WorkflowInstanceListItem, WorkflowInstanceSummary, InstanceFilter } from './workflow-types';
import { WorkflowBatchActionToolbar } from './workflow-batch-action-toolbar';
import { useWorkflowGridKeyboardNav } from './use-workflow-grid-keyboard-nav';
import { WorkflowGridRow } from './workflow-grid-row';
import { GRID_COLS, ROW_HEIGHT, COL_COUNT } from './workflow-grid-constants';

const { Title } = Typography;

interface Props {
  /** Flattened loaded pages from useWorkflowInstancesInfinite */
  instances?: WorkflowInstanceListItem[];
  /** KPI counts from useWorkflowInstancesSummary — independent of loaded window */
  summary?: WorkflowInstanceSummary;
  isLoading?: boolean;
  /** True when more pages are available from the cursor endpoint */
  hasNextPage?: boolean;
  /** True while a fetchNextPage call is in-flight */
  isFetchingNextPage?: boolean;
  /** Called when user clicks "Load more" */
  onLoadMore?: () => void;
  filter?: InstanceFilter;
  onFilterChange?: (f: InstanceFilter) => void;
}

const OVERSCAN = 5;
const SCROLL_HEIGHT = 600;

export function InstanceMonitoringDashboard({
  instances = [],
  summary,
  isLoading = false,
  hasNextPage = false,
  isFetchingNextPage = false,
  onLoadMore,
}: Props) {
  const { t } = useTranslation();
  const [selectedIds, setSelectedIds] = useState<string[]>([]);

  const scrollRef = useRef<HTMLDivElement>(null);

  // O(1) selection lookup — avoids O(n) includes() per virtual row during scroll (fix for 500+ instances)
  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);

  // Purge selectedIds that no longer exist in the current instances array.
  // instances only grows (cursor appends pages), so this handles filter resets cleanly.
  useEffect(() => {
    const validIds = new Set(instances.map((i) => i.id));
    setSelectedIds((prev) => prev.filter((id) => validIds.has(id)));
  }, [instances]);

  const virtualizer = useVirtualizer({
    count: instances.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: OVERSCAN,
  });
  const virtualRows = virtualizer.getVirtualItems();
  const totalSize = virtualizer.getTotalSize();

  const onToggleSelect = useCallback((rowIndex: number) => {
    const id = instances[rowIndex]?.id;
    if (!id) return;
    setSelectedIds((prev) =>
      prev.includes(id) ? prev.filter((sid) => sid !== id) : [...prev, id],
    );
  }, [instances]);

  const { focusedCell, setFocusedCell, handleKeyDown, getCellTabIndex } =
    useWorkflowGridKeyboardNav({
      rowCount: instances.length,
      colCount: COL_COUNT,
      virtualizer,
      onToggleSelect,
    });

  // Refocus effect — reapply focus after each render when focused cell enters the virtual window.
  useEffect(() => {
    if (!focusedCell) return;
    const isRowVisible = virtualRows.some((vr) => vr.index === focusedCell.row);
    if (!isRowVisible) return;
    const cell = scrollRef.current?.querySelector<HTMLElement>(
      `[data-row="${focusedCell.row}"][data-col="${focusedCell.col}"]`,
    );
    cell?.focus({ preventScroll: true });
  }, [focusedCell, virtualRows]);

  const handleCheckboxChange = useCallback((checked: boolean, id: string) => {
    setSelectedIds((prev) =>
      checked ? [...prev, id] : prev.filter((sid) => sid !== id),
    );
  }, []);

  // Tab-in UX: initialize focus at (0,0) on first Tab-in (WAI-ARIA compliant).
  const handleGridFocus = useCallback(() => {
    if (focusedCell === null) setFocusedCell({ row: 0, col: 0 });
  }, [focusedCell, setFocusedCell]);

  // ─── KPI values — sourced from summary prop, not derived from loaded instances ──
  // Safe defaults: 0 / null when summary is still loading.
  const kpiActive = summary?.active ?? 0;
  const kpiCompletedToday = summary?.completedToday ?? 0;
  const kpiSlaBreachRate = summary?.slaBreachRate ?? 0;
  const kpiTotal = summary?.total ?? 0;

  // ─── Select-all scope: loaded window only (RT-08) ──────────────────────────────
  // Scope is intentionally "loaded rows" not "all active across all pages".
  // Tooltip communicates this limitation to the user.
  const allLoadedSelected = instances.length > 0 && selectedIds.length === instances.length;
  const someLoadedSelected = selectedIds.length > 0 && selectedIds.length < instances.length;

  return (
    <div>
      <Title level={4} style={{ marginBottom: 16 }}>{t('workflow.instance.dashboardTitle')}</Title>

      {/* KPI cards — sourced from summary endpoint, independent of loaded window */}
      <Row gutter={16} style={{ marginBottom: 24 }}>
        <Col span={6}>
          <Card size="small">
            <Statistic title={t('workflow.instance.kpiActive')} value={kpiActive} />
          </Card>
        </Col>
        <Col span={6}>
          <Card size="small">
            <Statistic title={t('workflow.instance.kpiCompletedToday')} value={kpiCompletedToday} />
          </Card>
        </Col>
        <Col span={6}>
          <Card size="small">
            <Statistic
              title={t('workflow.instance.kpiSlaBreachRate')}
              value={kpiSlaBreachRate ?? 0}
              suffix="%"
              valueStyle={(kpiSlaBreachRate ?? 0) > 10 ? { color: '#cf1322' } : undefined}
            />
          </Card>
        </Col>
        <Col span={6}>
          <Card size="small">
            <Statistic title={t('workflow.instance.kpiTotal')} value={kpiTotal} />
          </Card>
        </Col>
      </Row>

      <Space style={{ marginBottom: 12 }}>
        <Title level={5} style={{ margin: 0 }}>{t('workflow.instance.tableTitle')}</Title>
      </Space>

      <WorkflowBatchActionToolbar
        selectedInstanceIds={selectedIds}
        onClearSelection={() => setSelectedIds([])}
      />

      {/* Loaded-of-total label — makes cursor window scope explicit to the user */}
      <div style={{ marginBottom: 8, color: '#666', fontSize: 12 }}>
        {t('workflow.instance.loadedOfTotal', { loaded: instances.length, total: kpiTotal })}
        {selectedIds.length > 0 && ` — ${selectedIds.length} ${t('workflow.instance.selected')}`}
      </div>

      {/* WAI-ARIA 1.2 grid */}
      <div
        role="grid"
        aria-rowcount={instances.length + 1}
        aria-colcount={COL_COUNT}
        aria-multiselectable="true"
        aria-label={t('workflow.instance.gridAriaLabel', { count: instances.length })}
        onKeyDown={handleKeyDown}
        onFocus={handleGridFocus}
      >
        {/* Sticky header row */}
        <div
          role="row"
          aria-rowindex={1}
          style={{
            display: 'grid',
            gridTemplateColumns: GRID_COLS,
            background: '#fafafa',
            borderBottom: '1px solid #f0f0f0',
            padding: '8px 0',
            fontWeight: 600,
            fontSize: 12,
            position: 'sticky',
            top: 0,
            zIndex: 1,
          }}
        >
          {/* RT-08: select-all scope = loaded window; tooltip communicates this */}
          <div role="columnheader" aria-colindex={1} style={{ paddingLeft: 8 }}>
            <Tooltip title={t('workflow.instance.selectAllLoadedHint')}>
              <Checkbox
                aria-label={t('workflow.instance.selectAll')}
                indeterminate={someLoadedSelected}
                checked={allLoadedSelected}
                onChange={(e) => {
                  setSelectedIds(e.target.checked ? instances.map((i) => i.id) : []);
                }}
              />
            </Tooltip>
          </div>
          <div role="columnheader" aria-colindex={2}>{t('workflow.instance.entityType')}</div>
          <div role="columnheader" aria-colindex={3}>{t('workflow.instance.entityId')}</div>
          <div role="columnheader" aria-colindex={4}>{t('workflow.instance.currentState')}</div>
          <div role="columnheader" aria-colindex={5}>{t('workflow.instance.started')}</div>
          <div role="columnheader" aria-colindex={6}>{t('workflow.instance.due')}</div>
          <div role="columnheader" aria-colindex={7}>{t('workflow.instance.status')}</div>
        </div>

        {/* Virtual scroll container */}
        <div
          ref={scrollRef}
          role="presentation"
          style={{ height: SCROLL_HEIGHT, overflowY: 'auto', minHeight: 200 }}
        >
          {isLoading ? (
            <div style={{ textAlign: 'center', padding: 40 }}>
              <Spin />
            </div>
          ) : (
            <div role="presentation" style={{ height: totalSize, position: 'relative' }}>
              {virtualRows.map((virtualRow) => {
                const inst = instances[virtualRow.index];
                const isSelected = selectedSet.has(inst.id);

                return (
                  <WorkflowGridRow
                    key={inst.id}
                    instance={inst}
                    rowIndex={virtualRow.index}
                    virtualStart={virtualRow.start}
                    measureRef={virtualizer.measureElement}
                    isSelected={isSelected}
                    onCheckboxChange={handleCheckboxChange}
                    getCellTabIndex={getCellTabIndex}
                  />
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Load-more footer — only shown when more pages are available */}
      {hasNextPage && (
        <div style={{ textAlign: 'center', marginTop: 16 }}>
          <Button
            onClick={onLoadMore}
            loading={isFetchingNextPage}
            disabled={isFetchingNextPage}
          >
            {t('workflow.instance.loadMore')}
          </Button>
        </div>
      )}
    </div>
  );
}
