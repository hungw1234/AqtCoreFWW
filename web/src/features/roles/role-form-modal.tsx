import { useEffect } from 'react';
import { Modal, Form, Input, Select } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCreateRole, useUpdateRole } from './roles-api';
import type { RoleDefinitionDto, CreateRoleRequest, UpdateRoleRequest } from './roles-api';
import { appMessage } from '@/core/api/notification-bridge';

interface RoleFormModalProps {
  open: boolean;
  editRole?: RoleDefinitionDto | null;
  onClose: () => void;
}

// RoleFormModal — create or edit a role (name, code, description, type)
export function RoleFormModal({ open, editRole, onClose }: RoleFormModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<CreateRoleRequest>();
  const roleTypeOptions = [
    { label: t('roles.typeSystem'), value: 1 },
    { label: t('roles.typeBusiness'), value: 2 },
  ];
  const isEdit = Boolean(editRole);
  const isSystem = editRole?.roleType === 1;

  const createMutation = useCreateRole();
  const updateMutation = useUpdateRole(editRole?.id ?? '');
  const isPending = createMutation.isPending || updateMutation.isPending;

  // Populate form when editing
  useEffect(() => {
    if (open && editRole) {
      form.setFieldsValue({
        name: editRole.name,
        code: editRole.code,
        description: editRole.description,
        roleType: editRole.roleType,
      });
    } else if (open) {
      form.resetFields();
      form.setFieldsValue({ roleType: 2 }); // Default to Business
    }
  }, [open, editRole, form]);

  async function handleOk() {
    try {
      const values = await form.validateFields();
      if (isEdit) {
        const body: UpdateRoleRequest = {
          name: values.name,
          description: values.description,
        };
        await updateMutation.mutateAsync(body);
        appMessage.success(t('roles.updateSuccess'));
      } else {
        await createMutation.mutateAsync(values);
        appMessage.success(t('roles.createSuccess'));
      }
      onClose();
    } catch {
      // validation or API error — antd form shows inline errors
    }
  }

  return (
    <Modal
      title={isEdit ? t('roles.editTitle') : t('roles.createTitle')}
      open={open}
      onOk={handleOk}
      onCancel={onClose}
      okText={isEdit ? t('common.save') : t('common.add')}
      cancelText={t('common.cancel')}
      confirmLoading={isPending}
      destroyOnHidden
      width={480}
    >
      <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
        <Form.Item
          name="name"
          label={t('roles.form.name')}
          rules={[
            { required: true, message: t('roles.form.nameRequired') },
            { max: 200, message: t('roles.form.maxChars', { max: 200 }) },
          ]}
        >
          <Input placeholder={t('roles.form.namePlaceholder')} />
        </Form.Item>

        <Form.Item
          name="code"
          label={t('roles.form.code')}
          rules={[
            { required: true, message: t('roles.form.codeRequired') },
            { max: 100, message: t('roles.form.maxChars', { max: 100 }) },
            { pattern: /^[A-Z0-9_]+$/, message: t('roles.form.codeFormat') },
          ]}
          normalize={(v: string) => v?.toUpperCase()}
        >
          <Input placeholder={t('roles.form.codePlaceholder')} disabled={isEdit} />
        </Form.Item>

        <Form.Item name="description" label={t('roles.form.description')}>
          <Input.TextArea rows={3} placeholder={t('roles.form.descPlaceholder')} />
        </Form.Item>

        <Form.Item name="roleType" label={t('roles.form.type')}>
          <Select options={roleTypeOptions} disabled={isEdit || isSystem} />
        </Form.Item>
      </Form>
    </Modal>
  );
}
