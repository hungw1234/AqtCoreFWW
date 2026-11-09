// view-column-editor.tsx — editable table for configuring view columns
// Supports drag-to-reorder via @dnd-kit with handle-only drag (icon scoped, not whole row)
import React, { useCallback, createContext, useContext } from 'react';
import { Table, Select, Input, InputNumber, Checkbox, Button } from 'antd';
import {
  PlusOutlined,
  DeleteOutlined,
  ArrowUpOutlined,
  ArrowDownOutlined,
  HolderOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import {
  DndContext,
  closestCenter,
  type DragEndEvent,
  PointerSensor,
  KeyboardSensor,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import type { DraggableAttributes } from '@dnd-kit/core';
// SyntheticListenerMap was removed from @dnd-kit/core public API in v6; extract from useSortable return type
type SyntheticListenerMap = NonNullable<ReturnType<typeof useSortable>['listeners']>;
import {
  SortableContext,
  useSortable,
  arrayMove,
  verticalListSortingStrategy,
  sortableKeyboardCoordinates,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import type { FormFieldDto } from '../form-types';
import type { ViewColumnDto, ColumnFormatter } from './view-types';

// ──────────────────────────────────────────────────────────────────────────────
// Context bridge: SortableRow exposes listeners/attributes to handle cell
// without coupling the cell renderer to SortableRow internals.
// ──────────────────────────────────────────────────────────────────────────────
interface SortableRowContextValue {
  listeners?: SyntheticListenerMap;
  attributes?: DraggableAttributes;
  disabled?: boolean;
}

const SortableRowContext = createContext<SortableRowContextValue>({});

// ──────────────────────────────────────────────────────────────────────────────
// SortableRow — thin <tr> wrapper; sets ref/transform on the row,
// exposes drag listeners via context (handle-only drag, not whole-row).
// Memoized so unaffected rows don't re-render during drag.
// ──────────────────────────────────────────────────────────────────────────────
const SortableRow = React.memo(function SortableRow({
  'data-row-key': rowKey,
  'data-disabled': dataDisabled,
  ...props
}: React.HTMLAttributes<HTMLTableRowElement> & { 'data-row-key'?: string; 'data-disabled'?: string }) {
  const isDisabled = dataDisabled === 'true';
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: rowKey ?? '', disabled: isDisabled });

  const style: React.CSSProperties = {
    ...props.style,
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
    cursor: isDragging ? 'grabbing' : undefined,
  };

  return (
    <SortableRowContext.Provider value={{ listeners, attributes, disabled: isDisabled }}>
      <tr {...props} ref={setNodeRef} style={style}>
        {props.children}
      </tr>
    </SortableRowContext.Provider>
  );
});

// ──────────────────────────────────────────────────────────────────────────────
// DragHandle — consumed inside the drag-handle column cell.
// Reads listeners/attributes from SortableRowContext so only the icon
// initiates drag (preserves row click / select semantics).
// ──────────────────────────────────────────────────────────────────────────────
function DragHandle({ ariaLabel }: { ariaLabel: string }) {
  const { listeners, attributes, disabled } = useContext(SortableRowContext);
  // When disabled: suppress all drag listeners/attributes and show non-interactive cursor
  return (
    <HolderOutlined
      {...(disabled ? {} : listeners)}
      {...(disabled ? {} : attributes)}
      aria-label={ariaLabel}
      aria-disabled={disabled}
      style={{ cursor: disabled ? 'not-allowed' : 'grab', color: disabled ? '#ccc' : '#999', touchAction: 'none' }}
    />
  );
}

// ──────────────────────────────────────────────────────────────────────────────
// Constants
// ──────────────────────────────────────────────────────────────────────────────
const FORMATTER_OPTIONS: { value: ColumnFormatter; label: string }[] = [
  { value: 'Text', label: 'Text' },
  { value: 'Date', label: 'Date' },
  { value: 'Currency', label: 'Currency' },
  { value: 'Badge', label: 'Badge' },
  { value: 'Link', label: 'Link' },
];

// ──────────────────────────────────────────────────────────────────────────────
// Props
// ──────────────────────────────────────────────────────────────────────────────
interface ViewColumnEditorProps {
  fields: FormFieldDto[];
  value?: ViewColumnDto[];
  onChange?: (cols: ViewColumnDto[]) => void;
  /** Defensively added: disables drag and edit inputs when true */
  disabled?: boolean;
}

// ──────────────────────────────────────────────────────────────────────────────
// Stable sort id helper — fieldName + index composite avoids clashes
// fieldName comes from validated Select options (not free-text), so no XSS risk
// ──────────────────────────────────────────────────────────────────────────────
function makeSortId(c: ViewColumnDto, i: number): string {
  return `col-${c.fieldName || 'empty'}-${i}`;
}

// ──────────────────────────────────────────────────────────────────────────────
// ViewColumnEditor
// ──────────────────────────────────────────────────────────────────────────────
export function ViewColumnEditor({ fields, value = [], onChange, disabled = false }: ViewColumnEditorProps) {
  const { t } = useTranslation();

  // Register PointerSensor + KeyboardSensor (a11y GOV requirement)
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const fieldOptions = fields
    .filter((f) => !['Section', 'Label', 'Divider'].includes(f.type))
    .map((f) => ({ value: f.fieldKey, label: f.labelVi || f.fieldKey }));

  function update(idx: number, patch: Partial<ViewColumnDto>) {
    const updated = [...value];
    updated[idx] = { ...updated[idx], ...patch };
    onChange?.(updated);
  }

  function remove(idx: number) {
    onChange?.(value.filter((_, i) => i !== idx));
  }

  function move(idx: number, dir: -1 | 1) {
    const target = idx + dir;
    if (target < 0 || target >= value.length) return;
    const updated = [...value];
    [updated[idx], updated[target]] = [updated[target], updated[idx]];
    // Recalc displayOrder
    onChange?.(updated.map((c, i) => ({ ...c, displayOrder: i + 1 })));
  }

  function addColumn() {
    onChange?.([
      ...value,
      {
        fieldName: '',
        displayOrder: value.length + 1,
        sortable: true,
        filterable: false,
        formatter: 'Text',
      },
    ]);
  }

  // useCallback with [value] dependency makes the stale-closure guard explicit
  const handleDragEnd = useCallback(
    (event: DragEndEvent) => {
      const { active, over } = event;
      if (!over || active.id === over.id) return;

      const sortIds = value.map(makeSortId);
      const oldIndex = sortIds.indexOf(String(active.id));
      const newIndex = sortIds.indexOf(String(over.id));
      if (oldIndex < 0 || newIndex < 0) return;

      const reordered = arrayMove(value, oldIndex, newIndex);
      onChange?.(reordered.map((c, i) => ({ ...c, displayOrder: i + 1 })));
    },
    [value, onChange],
  );

  // Stable sort ids (recomputed each render alongside value changes)
  const sortIds = value.map(makeSortId);

  const columns: ColumnsType<ViewColumnDto & { __sortId: string }> = [
    {
      // Drag handle column — handle-only drag via SortableRowContext
      title: '',
      key: 'dragHandle',
      width: 32,
      render: () => (
        // DragHandle reads listeners from context; only this icon starts drag
        <DragHandle ariaLabel={t('page.forms.views.dragHandle')} />
      ),
    },
    {
      title: 'Field', dataIndex: 'fieldName', width: 140,
      render: (val: string, _: ViewColumnDto, idx: number) => (
        <Select size="small" style={{ width: 130 }} value={val || undefined}
          disabled={disabled}
          onChange={(v) => {
            const f = fields.find((fi) => fi.fieldKey === v);
            update(idx, { fieldName: v, label: f?.labelVi || v });
          }}
          options={fieldOptions} />
      ),
    },
    {
      title: 'Label', dataIndex: 'label', width: 120,
      render: (val: string, _: ViewColumnDto, idx: number) => (
        <Input size="small" value={val} disabled={disabled}
          onChange={(e) => update(idx, { label: e.target.value })} />
      ),
    },
    {
      title: 'Sort', dataIndex: 'sortable', width: 50,
      render: (val: boolean, _: ViewColumnDto, idx: number) => (
        <Checkbox checked={val} disabled={disabled}
          onChange={(e) => update(idx, { sortable: e.target.checked })} />
      ),
    },
    {
      title: 'Filter', dataIndex: 'filterable', width: 50,
      render: (val: boolean, _: ViewColumnDto, idx: number) => (
        <Checkbox checked={val} disabled={disabled}
          onChange={(e) => update(idx, { filterable: e.target.checked })} />
      ),
    },
    {
      title: 'Width', dataIndex: 'width', width: 80,
      render: (val: number | undefined, _: ViewColumnDto, idx: number) => (
        <InputNumber size="small" style={{ width: 70 }} value={val} min={50} max={500}
          disabled={disabled}
          onChange={(v) => update(idx, { width: v ?? undefined })} />
      ),
    },
    {
      title: 'Format', dataIndex: 'formatter', width: 100,
      render: (val: string, _: ViewColumnDto, idx: number) => (
        <Select size="small" style={{ width: 90 }} value={val || 'Text'}
          disabled={disabled}
          onChange={(v) => update(idx, { formatter: v as ColumnFormatter })}
          options={FORMATTER_OPTIONS} />
      ),
    },
    {
      title: '', key: 'actions', width: 90,
      render: (_: unknown, __: ViewColumnDto, idx: number) => (
        <span>
          <Button type="text" size="small" icon={<ArrowUpOutlined />}
            disabled={disabled || idx <= 0}
            onClick={() => move(idx, -1)} />
          <Button type="text" size="small" icon={<ArrowDownOutlined />}
            disabled={disabled || idx >= value.length - 1}
            onClick={() => move(idx, 1)} />
          <Button type="text" size="small" danger icon={<DeleteOutlined />}
            disabled={disabled}
            onClick={() => remove(idx)} />
        </span>
      ),
    },
  ];

  const dataSource = value.map((c, i) => ({ ...c, __sortId: makeSortId(c, i) }));

  return (
    <>
      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragEnd={handleDragEnd}
      >
        <SortableContext items={sortIds} strategy={verticalListSortingStrategy}>
          <Table
            size="small"
            dataSource={dataSource}
            rowKey="__sortId"
            columns={columns}
            pagination={false}
            scroll={{ x: true }}
            components={{ body: { row: SortableRow } }}
            // Pass disabled to each row via data attribute so SortableRow can wire useSortable.disabled
            onRow={() => ({ 'data-disabled': String(disabled) } as React.HTMLAttributes<HTMLElement>)}
          />
        </SortableContext>
      </DndContext>
      <Button type="dashed" size="small" icon={<PlusOutlined />}
        disabled={disabled}
        onClick={addColumn} style={{ marginTop: 8 }} block>
        {t('page.forms.views.addColumn')}
      </Button>
    </>
  );
}
