// field-canvas-card.tsx — sortable field card rendered inside FieldCanvas
// Handles drag-to-reorder via useSortable; shows selected state, type badge, delete button

import { Tag, Space, Button, Popconfirm, theme } from 'antd';
import {
  HolderOutlined,
  DeleteOutlined,
  ExclamationCircleOutlined,
} from '@ant-design/icons';
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useTranslation } from 'react-i18next';
import { FIELD_TYPE_REGISTRY } from './field-type-registry';
import type { FormFieldDto } from '../form-types';

interface FieldCanvasCardProps {
  field: FormFieldDto;
  isSelected: boolean;
  onSelect: (fieldId: string) => void;
  onDelete: (fieldId: string) => void;
  /** Disables drag handle and delete button for non-Draft templates */
  disabled: boolean;
}

export function FieldCanvasCard({
  field,
  isSelected,
  onSelect,
  onDelete,
  disabled,
}: FieldCanvasCardProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();

  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: field.id,
    disabled,
  });

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.3 : 1,
    marginBottom: 6,
    border: isSelected
      ? `2px solid ${token.colorPrimary}`
      : `1px solid ${token.colorBorderSecondary}`,
    borderRadius: token.borderRadius,
    background: isSelected ? token.colorPrimaryBg : token.colorBgContainer,
    padding: '8px 10px',
    cursor: disabled ? 'default' : 'pointer',
    display: 'flex',
    alignItems: 'center',
    gap: 8,
    userSelect: 'none',
    boxShadow: isSelected ? `0 0 0 2px ${token.colorPrimaryBgHover}` : undefined,
  };

  const config = FIELD_TYPE_REGISTRY[field.type];

  return (
    <div ref={setNodeRef} style={style} onClick={() => onSelect(field.id)}>
      {/* Drag handle — only shown when editable */}
      {!disabled && (
        <HolderOutlined
          {...listeners}
          {...attributes}
          style={{
            cursor: 'grab',
            color: token.colorTextTertiary,
            fontSize: 14,
            flexShrink: 0,
          }}
          onClick={(e) => e.stopPropagation()}
        />
      )}

      {/* Field info */}
      <div style={{ flex: 1, minWidth: 0 }}>
        <Space size={4} wrap={false}>
          <span style={{ fontWeight: 500, fontSize: 13, color: token.colorText }}>
            {field.labelVi || field.fieldKey}
          </span>
          {field.required && (
            <ExclamationCircleOutlined
              style={{ color: token.colorError, fontSize: 11 }}
              title={t('forms.builder.canvas.required')}
            />
          )}
        </Space>
        <div style={{ fontSize: 11, color: token.colorTextSecondary, marginTop: 2 }}>
          <code style={{ fontSize: 10 }}>{field.fieldKey}</code>
        </div>
      </div>

      {/* Type badge */}
      <Tag
        color={config?.category === 'layout' ? 'default' : 'processing'}
        style={{ fontSize: 10, flexShrink: 0 }}
      >
        {field.type}
      </Tag>

      {/* Delete button */}
      {!disabled && (
        <Popconfirm
          title={t('forms.builder.canvas.deleteConfirm')}
          onConfirm={(e) => {
            e?.stopPropagation();
            onDelete(field.id);
          }}
          onCancel={(e) => e?.stopPropagation()}
          okType="danger"
        >
          <Button
            type="text"
            size="small"
            danger
            icon={<DeleteOutlined />}
            onClick={(e) => e.stopPropagation()}
            style={{ flexShrink: 0 }}
          />
        </Popconfirm>
      )}
    </div>
  );
}
