import { useEffect } from 'react';
import { Modal, Form, Input, Select } from 'antd';
import { useCreateOrgUnit, useUpdateOrgUnit } from './org-api';
import type { OrgUnitDto, CreateOrgUnitRequest } from './org-types';
import { appMessage } from '@/core/api/notification-bridge';

const { Option } = Select;

interface OrgUnitFormModalProps {
  open: boolean;
  editUnit?: OrgUnitDto | null;
  allUnits: OrgUnitDto[];  // for parent select
  onClose: () => void;
}

// OrgUnitFormModal — create or edit an org unit (name, code, parent)
export function OrgUnitFormModal({ open, editUnit, allUnits, onClose }: OrgUnitFormModalProps) {
  const [form] = Form.useForm<CreateOrgUnitRequest>();
  const isEdit = Boolean(editUnit);

  const createMutation = useCreateOrgUnit();
  const updateMutation = useUpdateOrgUnit(editUnit?.id ?? '');
  const isPending = createMutation.isPending || updateMutation.isPending;

  useEffect(() => {
    if (open && editUnit) {
      form.setFieldsValue({
        name: editUnit.name,
        code: editUnit.code,
        parentId: editUnit.parentId,
        description: editUnit.description,
      });
    } else if (open) {
      form.resetFields();
    }
  }, [open, editUnit, form]);

  async function handleOk() {
    try {
      const values = await form.validateFields();
      if (isEdit) {
        await updateMutation.mutateAsync(values);
        appMessage.success('Cập nhật đơn vị thành công');
      } else {
        await createMutation.mutateAsync(values);
        appMessage.success('Tạo đơn vị thành công');
      }
      onClose();
    } catch {
      // validation or API errors handled inline
    }
  }

  // Exclude current unit from parent options to prevent circular ref
  const parentOptions = allUnits.filter((u) => u.id !== editUnit?.id);

  return (
    <Modal
      title={isEdit ? 'Chỉnh sửa đơn vị' : 'Tạo đơn vị tổ chức'}
      open={open}
      onOk={handleOk}
      onCancel={onClose}
      okText={isEdit ? 'Lưu' : 'Tạo'}
      cancelText="Hủy"
      confirmLoading={isPending}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
        <Form.Item
          name="name"
          label="Tên đơn vị"
          rules={[{ required: true, message: 'Vui lòng nhập tên đơn vị' }]}
        >
          <Input placeholder="Phòng Công nghệ thông tin" />
        </Form.Item>

        <Form.Item
          name="code"
          label="Mã đơn vị"
          rules={[{ required: true, message: 'Vui lòng nhập mã đơn vị' }]}
        >
          <Input placeholder="CNTT-001" />
        </Form.Item>

        <Form.Item name="parentId" label="Đơn vị cha">
          <Select placeholder="Chọn đơn vị cha (để trống nếu là gốc)" allowClear showSearch
            filterOption={(input, opt) =>
              String(opt?.children ?? '').toLowerCase().includes(input.toLowerCase())
            }
          >
            {parentOptions.map((u) => (
              <Option key={u.id} value={u.id}>{u.name}</Option>
            ))}
          </Select>
        </Form.Item>

        <Form.Item name="description" label="Mô tả">
          <Input.TextArea rows={2} placeholder="Mô tả chức năng đơn vị..." />
        </Form.Item>
      </Form>
    </Modal>
  );
}
