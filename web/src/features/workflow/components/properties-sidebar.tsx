// Properties sidebar — Ant Design Drawer that opens when a node or edge is selected
// Node mode: edit state name, display names, isInitial, isFinal, color, sortOrder
// Edge mode: edit actionName, actionLabelVi, actionLabelEn, conditionsJson

import { useEffect } from 'react';
import { Drawer, Form, Input, Switch, InputNumber, ColorPicker, Button, Space } from 'antd';
import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import type { WorkflowStateDto, WorkflowTransitionDto, WorkflowBranchDto } from '../workflow-types';
import { TransitionAssignmentEditor } from './transition-assignment-editor';
import { BranchEditor } from './branch-editor';

export type SelectedElement =
  | { type: 'node'; data: WorkflowStateDto }
  | { type: 'edge'; data: WorkflowTransitionDto }
  | { type: 'gateway'; data: WorkflowBranchDto }
  | null;

interface PropertiesSidebarProps {
  selectedElement: SelectedElement;
  onUpdate: (element: SelectedElement) => void;
  onClose: () => void;
  /** Required when gateway elements may be selected — used by BranchEditor. */
  states?: WorkflowStateDto[];
  /** All branches in the graph — used to exclude already-forked states from the branching picker (RB3). */
  branches?: WorkflowBranchDto[];
}

export function PropertiesSidebar({ selectedElement, onUpdate, onClose, states, branches }: PropertiesSidebarProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm();

  // Reset form when selected element changes (gateway has no AntD Form fields — handled by BranchEditor)
  useEffect(() => {
    if (!selectedElement || selectedElement.type === 'gateway') return;
    form.setFieldsValue({ ...selectedElement.data });
  }, [selectedElement, form]);

  const handleSave = async () => {
    if (!selectedElement) return;
    // Gateway branches are committed directly via BranchEditor onChange — no form validation needed.
    if (selectedElement.type === 'gateway') return;
    // validateFields() rejects with { errorFields } when a required field is empty/invalid.
    // The rejection used to be swallowed by the onClick handler, so an invalid field scrolled
    // out of view in the 360px drawer made the button look dead. Surface it explicitly: the
    // warning toast guarantees feedback, and scrollToFirstError (on <Form>) brings the field in.
    const values = await form.validateFields().catch(() => null);
    if (!values) {
      appMessage.warning(
        t('workflow.designer.applyValidationFailed', 'Vui lòng kiểm tra các trường bắt buộc'),
      );
      return;
    }
    if (selectedElement.type === 'node') {
      const color =
        typeof values.color === 'string'
          ? values.color
          : (values.color?.toHexString?.() ?? selectedElement.data.color);
      onUpdate({ type: 'node', data: { ...selectedElement.data, ...values, color } });
    } else {
      onUpdate({ type: 'edge', data: { ...selectedElement.data, ...values } });
    }
    // "Áp dụng" only commits to the in-canvas graph (local state); persistence to the backend
    // happens on the toolbar "Lưu" (SaveDefinitionGraph). Toast makes that two-step model explicit
    // so a successful edit no longer feels like a no-op.
    appMessage.success(
      t('workflow.designer.applied', 'Đã áp dụng — nhấn Lưu để lưu xuống hệ thống'),
    );
  };

  const title =
    selectedElement?.type === 'node'
      ? t('workflow.designer.stateProperties', 'Thuộc tính bước')
      : selectedElement?.type === 'gateway'
        ? t('workflow.designer.gatewayProperties', 'Thuộc tính nhánh song song')
        : t('workflow.designer.transitionProperties', 'Thuộc tính hành động');

  return (
    <Drawer
      title={title}
      open={!!selectedElement}
      onClose={onClose}
      width={360}
      footer={
        <Space>
          {selectedElement?.type !== 'gateway' && (
            <Button type="primary" onClick={handleSave} size="small">
              {t('common.apply', 'Áp dụng')}
            </Button>
          )}
          <Button onClick={onClose} size="small">
            {t('common.cancel', 'Hủy')}
          </Button>
        </Space>
      }
    >
      {selectedElement?.type === 'gateway' && (
        <BranchEditor
          value={selectedElement.data}
          states={states ?? []}
          branchedStateIds={new Set((branches ?? []).map((b) => String(b.branchingStateId)))}
          onChange={(updated) => onUpdate({ type: 'gateway', data: updated })}
        />
      )}
      <Form form={form} layout="vertical" size="small" scrollToFirstError>
        {selectedElement?.type === 'node' && (
          <>
            <Form.Item name="name" label={t('workflow.stateName', 'Tên hệ thống')} rules={[{ required: true }]}>
              <Input placeholder="e.g. Draft, UnderReview" />
            </Form.Item>
            <Form.Item name="displayNameVi" label={t('workflow.displayNameVi', 'Tên tiếng Việt')} rules={[{ required: true }]}>
              <Input placeholder="Tên hiển thị tiếng Việt" />
            </Form.Item>
            <Form.Item name="displayNameEn" label={t('workflow.displayNameEn', 'Tên tiếng Anh')} rules={[{ required: true }]}>
              <Input placeholder="English display name" />
            </Form.Item>
            <div style={{ display: 'flex', gap: 16 }}>
              <Form.Item name="isInitial" label={t('workflow.isInitial', 'Bước đầu')} valuePropName="checked">
                <Switch />
              </Form.Item>
              <Form.Item name="isFinal" label={t('workflow.isFinal', 'Bước cuối')} valuePropName="checked">
                <Switch />
              </Form.Item>
            </div>
            <div style={{ display: 'flex', gap: 16 }}>
              <Form.Item name="color" label={t('workflow.color', 'Màu sắc')}>
                <ColorPicker />
              </Form.Item>
              <Form.Item name="sortOrder" label={t('workflow.sortOrder', 'Thứ tự')}>
                <InputNumber min={0} style={{ width: 80 }} />
              </Form.Item>
            </div>
          </>
        )}

        {selectedElement?.type === 'edge' && (
          <>
            <Form.Item name="actionName" label={t('workflow.actionName', 'Tên hành động')} rules={[{ required: true }]}>
              <Input placeholder="e.g. Submit, Approve" />
            </Form.Item>
            <Form.Item name="actionLabelVi" label={t('workflow.actionLabelVi', 'Nhãn tiếng Việt')} rules={[{ required: true }]}>
              <Input placeholder="Nhãn hiển thị tiếng Việt" />
            </Form.Item>
            <Form.Item name="actionLabelEn" label={t('workflow.actionLabelEn', 'Nhãn tiếng Anh')} rules={[{ required: true }]}>
              <Input placeholder="English action label" />
            </Form.Item>
            <Form.Item name="conditionsJson" label={t('workflow.conditionsJson', 'Điều kiện (JSON)')}>
              <Input.TextArea rows={4} placeholder='{"field":"value"}' />
            </Form.Item>
            <Form.Item
              name="defaultAssigneeId"
              label={t('workflow.defaultAssigneeId', 'Người phê duyệt mặc định')}
            >
              <Input placeholder="UUID người phê duyệt (tuỳ chọn)" />
            </Form.Item>
            <Form.Item name="sortOrder" label={t('workflow.sortOrder', 'Thứ tự')}>
              <InputNumber min={0} style={{ width: 80 }} />
            </Form.Item>
            {/* M1 fold (code-review 260523-1730): force editor remount per-edge so
                Collapse defaultActiveKey re-evaluates against the new selection
                (AntD Drawer keeps children mounted across selection changes). */}
            <Form.Item name="assignment" valuePropName="value" noStyle key={selectedElement.data.id}>
              <TransitionAssignmentEditor />
            </Form.Item>
          </>
        )}
      </Form>
    </Drawer>
  );
}
