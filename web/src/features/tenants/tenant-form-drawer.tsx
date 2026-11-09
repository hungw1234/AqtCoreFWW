import { useEffect } from 'react';
import { Drawer, Form, Input, Button, Space } from 'antd';
import { useCreateTenant, useUpdateTenant } from './tenant-api';
import type { TenantAdminDto } from './tenant-types';
import { appMessage } from '@/core/api/notification-bridge';

interface TenantFormValues {
  name: string;
  code: string;
  contactEmail?: string;
}

interface TenantFormDrawerProps {
  open: boolean;
  mode: 'create' | 'edit';
  tenant?: TenantAdminDto;
  onClose: () => void;
}

export function TenantFormDrawer({ open, mode, tenant, onClose }: TenantFormDrawerProps) {
  const [form] = Form.useForm<TenantFormValues>();
  const createMutation = useCreateTenant();
  const updateMutation = useUpdateTenant(tenant?.id ?? '');

  useEffect(() => {
    if (open) {
      form.setFieldsValue({
        name: tenant?.name ?? '',
        code: tenant?.code ?? '',
        contactEmail: tenant?.contactEmail ?? '',
      });
    } else {
      form.resetFields();
    }
  }, [open, tenant, form]);

  async function handleSubmit() {
    let values: TenantFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // Antd reject when validation fails — handled by inline field errors.
      return;
    }

    try {
      if (mode === 'create') {
        await createMutation.mutateAsync({
          name: values.name,
          code: values.code,
          contactEmail: values.contactEmail || null,
        });
        appMessage.success('Đã tạo tenant');
      } else {
        await updateMutation.mutateAsync({
          name: values.name,
          contactEmail: values.contactEmail || null,
        });
        appMessage.success('Đã cập nhật tenant');
      }
      onClose();
    } catch (err) {
      const errorMsg =
        (err as { message?: string } | undefined)?.message ?? 'Thao tác thất bại';
      appMessage.error(errorMsg);
    }
  }

  const submitting = createMutation.isPending || updateMutation.isPending;

  return (
    <Drawer
      title={mode === 'create' ? 'Tạo tenant mới' : 'Sửa tenant'}
      width={480}
      open={open}
      onClose={onClose}
      destroyOnClose
      extra={
        <Space>
          <Button onClick={onClose}>Hủy</Button>
          <Button type="primary" loading={submitting} onClick={() => void handleSubmit()}>
            {mode === 'create' ? 'Tạo' : 'Lưu'}
          </Button>
        </Space>
      }
    >
      <Form form={form} layout="vertical" preserve={false}>
        <Form.Item
          label="Tên tenant"
          name="name"
          rules={[
            { required: true, message: 'Bắt buộc nhập' },
            { max: 200, message: 'Tối đa 200 ký tự' },
          ]}
        >
          <Input placeholder="Bộ Tài chính" />
        </Form.Item>

        <Form.Item
          label="Mã tenant"
          name="code"
          tooltip={mode === 'edit' ? 'Mã không thể thay đổi sau khi tạo' : undefined}
          rules={[
            { required: true, message: 'Bắt buộc nhập' },
            { max: 50, message: 'Tối đa 50 ký tự' },
            { pattern: /^[A-Z0-9_-]+$/, message: 'Chỉ chữ HOA, số, dấu - hoặc _' },
          ]}
        >
          <Input placeholder="TENANT-MOFA01" disabled={mode === 'edit'} />
        </Form.Item>

        <Form.Item
          label="Email liên hệ"
          name="contactEmail"
          rules={[{ type: 'email', message: 'Email không hợp lệ' }]}
        >
          <Input placeholder="admin@example.gov.vn" />
        </Form.Item>
      </Form>
    </Drawer>
  );
}
