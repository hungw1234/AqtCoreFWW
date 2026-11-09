// field-canvas.tsx — center drop zone: sortable list of template fields
// Accepts drops from palette (new fields) and supports drag-to-reorder

import { Typography, theme } from 'antd';
import { InboxOutlined } from '@ant-design/icons';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { useDroppable } from '@dnd-kit/core';
import { useTranslation } from 'react-i18next';
import { FieldCanvasCard } from './field-canvas-card';
import type { FormFieldDto } from '../form-types';

const { Text } = Typography;

export const CANVAS_DROPPABLE_ID = 'canvas';

interface FieldCanvasProps {
  /** Fields sorted by displayOrder */
  fields: FormFieldDto[];
  selectedFieldId: string | null;
  onSelectField: (id: string) => void;
  onDeleteField: (id: string) => void;
  /** True for non-Draft templates — disables all mutations */
  disabled: boolean;
}

export function FieldCanvas({
  fields,
  selectedFieldId,
  onSelectField,
  onDeleteField,
  disabled,
}: FieldCanvasProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  // Make entire canvas a valid drop target (catches palette drops on empty canvas)
  const { setNodeRef, isOver } = useDroppable({ id: CANVAS_DROPPABLE_ID });

  return (
    <div
      ref={setNodeRef}
      style={{
        flex: 1,
        overflowY: 'auto',
        padding: 16,
        background: isOver ? token.colorPrimaryBg : token.colorBgLayout,
        transition: 'background 0.15s',
        minHeight: 0,
      }}
    >
      <SortableContext
        items={fields.map((f) => f.id)}
        strategy={verticalListSortingStrategy}
      >
        {fields.length === 0 ? (
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              height: '100%',
              minHeight: 300,
              color: token.colorTextTertiary,
              border: `2px dashed ${isOver ? token.colorPrimary : token.colorBorderSecondary}`,
              borderRadius: token.borderRadius,
              padding: 32,
              gap: 8,
            }}
          >
            <InboxOutlined style={{ fontSize: 40 }} />
            <Text type="secondary" style={{ textAlign: 'center' }}>
              {t('forms.builder.canvas.emptyHint')}
            </Text>
          </div>
        ) : (
          fields.map((field) => (
            <FieldCanvasCard
              key={field.id}
              field={field}
              isSelected={field.id === selectedFieldId}
              onSelect={onSelectField}
              onDelete={onDeleteField}
              disabled={disabled}
            />
          ))
        )}
      </SortableContext>
    </div>
  );
}
