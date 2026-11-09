// field-palette.tsx — left sidebar: draggable field-type cards grouped by category
// Palette items are drag sources (useDraggable), not sortable

import { useState } from 'react';
import { Collapse, Input, Typography, theme } from 'antd';
import { useDraggable } from '@dnd-kit/core';
import { useTranslation } from 'react-i18next';
import {
  FontSizeOutlined, NumberOutlined, CalendarOutlined, AlignLeftOutlined,
  CheckSquareOutlined, PaperClipOutlined, EditOutlined, UnorderedListOutlined,
  LinkOutlined, GlobalOutlined, TableOutlined, EnvironmentOutlined,
  FieldTimeOutlined, FunctionOutlined, BarsOutlined, TagOutlined, MinusOutlined,
} from '@ant-design/icons';
import {
  FIELD_CATEGORIES,
  FIELD_TYPE_REGISTRY,
  getByCategory,
  type FieldCategory,
  type FieldTypeConfig,
} from './field-type-registry';
import type { FormFieldType } from '../form-types';

const { Text } = Typography;

/** Icon map: field type → Ant Design icon component */
const ICON_MAP: Record<string, React.ReactNode> = {
  FontSizeOutlined: <FontSizeOutlined />,
  NumberOutlined: <NumberOutlined />,
  CalendarOutlined: <CalendarOutlined />,
  AlignLeftOutlined: <AlignLeftOutlined />,
  CheckSquareOutlined: <CheckSquareOutlined />,
  PaperClipOutlined: <PaperClipOutlined />,
  EditOutlined: <EditOutlined />,
  UnorderedListOutlined: <UnorderedListOutlined />,
  LinkOutlined: <LinkOutlined />,
  GlobalOutlined: <GlobalOutlined />,
  TableOutlined: <TableOutlined />,
  EnvironmentOutlined: <EnvironmentOutlined />,
  FieldTimeOutlined: <FieldTimeOutlined />,
  FunctionOutlined: <FunctionOutlined />,
  BarsOutlined: <BarsOutlined />,
  TagOutlined: <TagOutlined />,
  MinusOutlined: <MinusOutlined />,
};

/** Drag data shape — checked in form-builder-page.tsx onDragEnd */
export interface PaletteDragData {
  source: 'palette';
  fieldType: FormFieldType;
}

interface DraggablePaletteCardProps {
  config: FieldTypeConfig;
  disabled: boolean;
}

function DraggablePaletteCard({ config, disabled }: DraggablePaletteCardProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: `palette-${config.type}`,
    data: { source: 'palette', fieldType: config.type } satisfies PaletteDragData,
    disabled,
  });

  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 4,
        padding: '8px 4px',
        borderRadius: token.borderRadius,
        border: `1px solid ${token.colorBorderSecondary}`,
        background: token.colorBgContainer,
        cursor: disabled ? 'not-allowed' : 'grab',
        opacity: isDragging ? 0.4 : disabled ? 0.5 : 1,
        userSelect: 'none',
        fontSize: 18,
        color: disabled ? token.colorTextDisabled : token.colorPrimary,
        transition: 'box-shadow 0.15s',
      }}
    >
      {ICON_MAP[config.icon]}
      <Text style={{ fontSize: 11, textAlign: 'center', lineHeight: 1.2 }}>
        {t(config.labelKey)}
      </Text>
    </div>
  );
}

const CATEGORY_LABEL_KEYS: Record<FieldCategory, string> = {
  basic: 'forms.builder.category.basic',
  reference: 'forms.builder.category.reference',
  complex: 'forms.builder.category.complex',
  computed: 'forms.builder.category.computed',
  layout: 'forms.builder.category.layout',
};

interface FieldPaletteProps {
  /** Disable dragging — true when template is not Draft */
  disabled: boolean;
}

export function FieldPalette({ disabled }: FieldPaletteProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const [search, setSearch] = useState('');

  const filtered = search.trim()
    ? Object.values(FIELD_TYPE_REGISTRY).filter((c) =>
        t(c.labelKey).toLowerCase().includes(search.toLowerCase())
      )
    : null;

  const collapseItems = FIELD_CATEGORIES.map((cat) => {
    const configs = filtered
      ? filtered.filter((c) => c.category === cat)
      : getByCategory(cat);
    if (configs.length === 0) return null;

    return {
      key: cat,
      label: t(CATEGORY_LABEL_KEYS[cat]),
      children: (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 6 }}>
          {configs.map((cfg) => (
            <DraggablePaletteCard key={cfg.type} config={cfg} disabled={disabled} />
          ))}
        </div>
      ),
    };
  }).filter(Boolean) as NonNullable<import('antd').CollapseProps['items']>;

  return (
    <div
      style={{
        width: 220,
        minWidth: 220,
        borderRight: `1px solid ${token.colorBorderSecondary}`,
        overflowY: 'auto',
        display: 'flex',
        flexDirection: 'column',
        background: token.colorBgLayout,
      }}
    >
      <div style={{ padding: '10px 10px 6px' }}>
        <Input.Search
          size="small"
          placeholder={t('forms.builder.palette.search')}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          allowClear
        />
      </div>
      <Collapse
        size="small"
        defaultActiveKey={FIELD_CATEGORIES as unknown as string[]}
        ghost
        items={collapseItems}
        style={{ padding: '0 4px' }}
      />
    </div>
  );
}
