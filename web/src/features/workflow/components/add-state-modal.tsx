// Add State modal — form for creating a new workflow state in the designer
// Extracted from definition-visual-designer-tab to stay under 200-line limit

import { Modal, Form, Input, Switch, InputNumber, ColorPicker } from 'antd';
import { useTranslation } from 'react-i18next';
import type { WorkflowStateDto } from '../workflow-types';

interface AddStateModalProps {
  open: boolean;
  nextSortOrder: number;
  onAdd: (state: WorkflowStateDto) => void;
  onCancel: () => void;
}

export function AddStateModal({ open, nextSortOrder, onAdd, onCancel }: AddStateModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm();

  const handleOk = async () => {
    const values = await form.validateFields();
    const color =
      typeof values.color === 'string'
        ? values.color
        : (values.color?.toHexString?.() ?? '#808080');
    onAdd({
      id: `new-${Date.now()}`,
      name: values.name,
      displayNameVi: values.displayNameVi,
      displayNameEn: values.displayNameEn,
      isInitial: values.isInitial ?? false,
      isFinal: values.isFinal ?? false,
      color,
      sortOrder: nextSortOrder,
    });
    form.resetFields();
  };

  const handleCancel = () => {
    form.resetFields();
    onCancel();
  };

  return (
    <Modal
      title={t('workflow.addState', 'Thêm bước mới')}
      open={open}
      onOk={handleOk}
      onCancel={handleCancel}
      okText={t('common.add', 'Thêm')}
      cancelText={t('common.cancel', 'Hủy')}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={{ color: '#808080', sortOrder: nextSortOrder, isInitial: false, isFinal: false }}
        size="small"
      >
        <Form.Item name="name" label={t('workflow.stateName', 'Tên hệ thống')} rules={[{ required: true }]}>
          <Input placeholder="e.g. Draft, UnderReview, Approved" />
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
            <InputNumber min={0} />
          </Form.Item>
        </div>
      </Form>
    </Modal>
  );
}
