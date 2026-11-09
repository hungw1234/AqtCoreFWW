// form-builder-page.tsx — main 3-column form builder page
// Composes: FieldPalette | FieldCanvas | FieldPropertiesPanel inside a DndContext
// Below the builder canvas: Submissions / Analytics / Version Diff tabs (non-Draft templates only)

import { useState, useMemo, useCallback } from 'react';
import { Alert, Tabs } from 'antd';
import { CenteredSpin } from '@/shared/components/centered-spin';
import { appMessage } from '@/core/api/notification-bridge';
import {
  DndContext,
  DragOverlay,
  closestCenter,
  pointerWithin,
  type DragEndEvent,
  type DragStartEvent,
  type CollisionDetection,
} from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { useParams } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';

import { useFormTemplate, useAddField, useBulkReorderFields, useDeactivateField, usePublishTemplate, useUpdateField } from '../form-api';
import type { FormFieldType, UpdateFieldPayload } from '../form-types';

import { FieldPalette, type PaletteDragData } from './field-palette';
import { FieldCanvas, CANVAS_DROPPABLE_ID } from './field-canvas';
import { FieldCanvasCard } from './field-canvas-card';
import { FieldPropertiesPanel } from './field-properties-panel';
import { FormBuilderToolbar } from './form-builder-toolbar';
import { FormPreviewModal } from './form-preview-modal';
import { FIELD_TYPE_REGISTRY } from './field-type-registry';
import { FormSubmissionsSection } from '../form-submissions-section';
import { FormAnalyticsPanel } from '../form-analytics-panel';
import { FormVersionDiffPanel } from '../form-version-diff-panel';

/** Generate a collision detection strategy that prefers canvas droppable for palette items */
const builderCollision: CollisionDetection = (args) => {
  const pointerCollisions = pointerWithin(args);
  if (pointerCollisions.length > 0) return pointerCollisions;
  return closestCenter(args);
};

/** Auto-generate a fieldKey from type + random suffix */
function generateFieldKey(type: FormFieldType): string {
  const suffix = Math.random().toString(36).slice(2, 6);
  return `${type.toLowerCase()}_${suffix}`;
}

export function FormBuilderPage() {
  const { t } = useTranslation();
  const { id } = useParams({ strict: false }) as { id: string };

  const { data: template, isLoading } = useFormTemplate(id);

  const [selectedFieldId, setSelectedFieldId] = useState<string | null>(null);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [activeDragId, setActiveDragId] = useState<string | null>(null);

  const addFieldMutation = useAddField(id);
  const bulkReorderMutation = useBulkReorderFields(id);
  const deactivateFieldMutation = useDeactivateField(id);
  const publishMutation = usePublishTemplate();
  const updateFieldMutation = useUpdateField(id);

  const isEditable = template?.status === 'Draft';

  const sortedFields = useMemo(
    () => [...(template?.fields ?? [])].filter((f) => f.isActive).sort((a, b) => a.displayOrder - b.displayOrder),
    [template?.fields]
  );

  const selectedField = sortedFields.find((f) => f.id === selectedFieldId) ?? null;

  // Active drag item — for DragOverlay rendering
  const activeDragField = activeDragId?.startsWith('palette-')
    ? null
    : sortedFields.find((f) => f.id === activeDragId) ?? null;
  const activePaletteType = activeDragId?.startsWith('palette-')
    ? (activeDragId.replace('palette-', '') as FormFieldType)
    : null;

  const handleDragStart = useCallback((event: DragStartEvent) => {
    setActiveDragId(String(event.active.id));
  }, []);

  const handleDragCancel = useCallback(() => {
    setActiveDragId(null);
  }, []);

  const handleDragEnd = useCallback(
    (event: DragEndEvent) => {
      setActiveDragId(null);
      const { active, over } = event;
      if (!over || !isEditable) return;

      const activeData = active.data.current as PaletteDragData | undefined;

      // Case 1: Palette → Canvas (new field drop)
      if (activeData?.source === 'palette') {
        const fieldType = activeData.fieldType;
        const overId = String(over.id);
        const overIndex = sortedFields.findIndex((f) => f.id === overId);
        const displayOrder = overIndex >= 0 ? overIndex : sortedFields.length;

        // Build payload — reference/formula types need default JSON to pass BE validation
        const config = FIELD_TYPE_REGISTRY[fieldType];
        addFieldMutation.mutate(
          {
            fieldKey: generateFieldKey(fieldType),
            type: fieldType,
            labelVi: t(config.labelKey),
            labelEn: fieldType,
            displayOrder,
            required: false,
            ...(config.hasDataSource ? { dataSourceJson: '{}' } : {}),
            ...(config.hasFormula ? { formulaJson: '{}' } : {}),
          },
          {
            onSuccess: (newField) => {
              if (newField?.id) setSelectedFieldId(newField.id);
            },
            onError: () => {
              appMessage.error(t('common.error', { defaultValue: 'Operation failed' }));
            },
          }
        );
        return;
      }

      // Case 2: Canvas reorder
      if (
        String(active.id) !== String(over.id) &&
        String(over.id) !== CANVAS_DROPPABLE_ID
      ) {
        const oldIndex = sortedFields.findIndex((f) => f.id === String(active.id));
        const newIndex = sortedFields.findIndex((f) => f.id === String(over.id));
        if (oldIndex < 0 || newIndex < 0) return;

        const reordered = arrayMove(sortedFields, oldIndex, newIndex);
        bulkReorderMutation.mutate(
          reordered.map((f, i) => ({ fieldId: f.id, newOrder: i })),
          {
            onError: () => {
              appMessage.error(t('common.error', { defaultValue: 'Reorder failed' }));
            },
          }
        );
      }
    },
    [isEditable, sortedFields, t, addFieldMutation, bulkReorderMutation]
  );

  const handleDeleteField = useCallback(
    (fieldId: string) => {
      deactivateFieldMutation.mutate(
        { fieldId },
        {
          onSuccess: () => {
            if (selectedFieldId === fieldId) setSelectedFieldId(null);
          },
          onError: () => {
            appMessage.error(t('common.error', { defaultValue: 'Delete failed' }));
          },
        }
      );
    },
    [deactivateFieldMutation, selectedFieldId, t]
  );

  const handleSaveField = useCallback(
    (fieldId: string, payload: UpdateFieldPayload) => {
      updateFieldMutation.mutate(
        { fieldId, ...payload },
        {
          onSuccess: () => {
            appMessage.success(t('common.save', { defaultValue: 'Saved' }));
          },
          onError: (err: unknown) => {
            const apiErr = err as { status?: number; message?: string };
            if (apiErr?.status === 409) {
              appMessage.warning(apiErr.message || t('common.conflictRefresh', { defaultValue: 'Data was modified. Refreshing...' }));
            } else {
              appMessage.error(t('common.error', { defaultValue: 'Save failed' }));
            }
          },
        }
      );
    },
    [updateFieldMutation, t]
  );

  const handlePublish = useCallback(() => {
    publishMutation.mutate(id, {
      onError: () => {
        appMessage.error(t('common.error', { defaultValue: 'Publish failed' }));
      },
    });
  }, [publishMutation, id, t]);

  if (isLoading) {
    return <CenteredSpin tip={t('common.loading')} style={{ display: 'block', marginTop: 48 }} />;
  }
  if (!template) {
    return <Alert type="error" message={t('page.forms.detail.notFound')} />;
  }

  // Tabs for non-Draft templates — submissions, analytics, version diff
  const detailTabs = template.status !== 'Draft'
    ? [
        {
          key: 'submissions',
          label: t('page.forms.detail.submissionsSection', { count: template.submissionsCount }),
          children: <FormSubmissionsSection templateId={id} />,
        },
        {
          key: 'analytics',
          label: t('page.forms.detail.analyticsTab', { defaultValue: 'Analytics' }),
          children: <FormAnalyticsPanel templateId={id} />,
        },
        {
          key: 'diff',
          label: t('page.forms.detail.versionDiffTab', { defaultValue: 'Version Diff' }),
          children: <FormVersionDiffPanel templateId={id} currentVersion={template.version ?? 1} />,
        },
      ]
    : null;

  // When detail tabs are present, allow the outer container to flex-shrink the canvas section
  const hasDetailTabs = Boolean(detailTabs);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: 'calc(100vh - 64px)', overflow: 'hidden' }}>
      <FormBuilderToolbar
        template={template}
        onPublish={handlePublish}
        onPreview={() => setPreviewOpen(true)}
        isPublishing={publishMutation.isPending}
      />

      <DndContext
        collisionDetection={builderCollision}
        onDragStart={handleDragStart}
        onDragEnd={handleDragEnd}
        onDragCancel={handleDragCancel}
      >
        {/* flex is reduced when detail tabs are shown below to give tabs visible space */}
        <div style={{ display: 'flex', flex: hasDetailTabs ? '0 0 55%' : 1, overflow: 'hidden' }}>
          <FieldPalette disabled={!isEditable} />

          <FieldCanvas
            fields={sortedFields}
            selectedFieldId={selectedFieldId}
            onSelectField={setSelectedFieldId}
            onDeleteField={handleDeleteField}
            disabled={!isEditable}
          />

          <FieldPropertiesPanel
            field={selectedField}
            templateId={id}
            allFields={sortedFields}
            disabled={!isEditable}
            onSave={handleSaveField}
            onDelete={handleDeleteField}
          />
        </div>

        {/* Drag overlay — ghost card during drag */}
        <DragOverlay dropAnimation={null}>
          {activePaletteType && (
            <div
              style={{
                padding: '6px 10px',
                background: '#fff',
                border: '1px solid #1677ff',
                borderRadius: 6,
                fontSize: 12,
                boxShadow: '0 4px 12px rgba(0,0,0,0.15)',
              }}
            >
              {t(FIELD_TYPE_REGISTRY[activePaletteType].labelKey)}
            </div>
          )}
          {activeDragField && (
            <FieldCanvasCard
              field={activeDragField}
              isSelected={false}
              onSelect={() => {}}
              onDelete={() => {}}
              disabled
            />
          )}
        </DragOverlay>
      </DndContext>

      <FormPreviewModal
        open={previewOpen}
        onClose={() => setPreviewOpen(false)}
        fields={sortedFields}
        templateName={template.name}
      />

      {/* Submissions / Analytics / Version Diff — shown only for non-Draft templates.
          Height capped at 40vh so the builder canvas remains usable above. */}
      {detailTabs && (
        <div style={{ maxHeight: '40vh', overflowY: 'auto', borderTop: '1px solid #f0f0f0', padding: '8px 16px', flexShrink: 0 }}>
          <Tabs items={detailTabs} defaultActiveKey="submissions" size="small" />
        </div>
      )}
    </div>
  );
}
