import { useEffect } from 'react';
import { Modal, Form, Input, Select, TreeSelect, Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCreateUser, useUpdateUser, useAssignStaff, useUser } from './user-api';
import { useOrgUnits } from '@/features/organization/org-api';
import { UserRole } from './user-types';
import type { UserDto, CreateUserRequest, UpdateUserRequest } from './user-types';
import type { OrgUnitDto } from '@/features/organization/org-types';
import { appMessage } from '@/core/api/notification-bridge';

const ROLE_OPTIONS = Object.values(UserRole).map((r) => ({ label: r, value: r }));

// Common gov role-in-org options for Vietnamese government structure
const ROLE_IN_ORG_OPTIONS = [
  'Lãnh đạo', 'Trưởng phòng', 'Phó trưởng phòng',
  'Chuyên viên chính', 'Chuyên viên', 'Nhân viên',
].map((r) => ({ label: r, value: r }));

// OrgTreeNode — recursive TreeSelect data node (structurally compatible with antd DataNode)
interface OrgTreeNode { value: string; title: string; children: OrgTreeNode[] }

// Build tree data from flat org unit list for TreeSelect
function buildOrgTree(units: OrgUnitDto[]) {
  const map = new Map<string, OrgTreeNode>();
  const roots: OrgTreeNode[] = [];

  for (const u of units) {
    map.set(u.id, { value: u.id, title: `${u.name} (${u.code})`, children: [] });
  }
  for (const u of units) {
    const node = map.get(u.id)!;
    if (u.parentId && map.has(u.parentId)) {
      map.get(u.parentId)!.children.push(node);
    } else {
      roots.push(node);
    }
  }
  return roots;
}

interface UserFormModalProps {
  open: boolean;
  editUser?: UserDto | null;
  onClose: () => void;
}

// UserFormModal — create or edit a user (name, email, roles, department, org unit assignment)
export function UserFormModal({ open, editUser, onClose }: UserFormModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<CreateUserRequest & { orgUnitId?: string; roleInOrg?: string }>();
  const isEdit = Boolean(editUser);

  const createMutation = useCreateUser();
  const updateMutation = useUpdateUser(editUser?.id ?? '');
  const assignStaffMutation = useAssignStaff();
  const { data: orgUnits = [] } = useOrgUnits();

  // Fetch the full detail when editing — list rows never carry signingNationalId (CCCD), so
  // prefilling from `editUser` alone would send an empty CCCD on save and wipe the stored value.
  // GetById returns the decrypted CCCD (caller has IDENTITY.USER.UPDATE to reach this modal).
  const { data: editDetail, isLoading: detailLoading, isError: detailError } = useUser(
    open && editUser ? editUser.id : '',
  );

  const isPending = createMutation.isPending || updateMutation.isPending || assignStaffMutation.isPending;
  const treeData = buildOrgTree(orgUnits);

  // Populate form when editing. signingNationalId comes from the detail fetch (editDetail), so the
  // effect re-runs once it loads; until then the CCCD field stays empty and save is disabled below.
  useEffect(() => {
    if (open && editUser) {
      form.setFieldsValue({
        fullName: editUser.fullName,
        email: editUser.email,
        department: editUser.departmentCode,
        roles: editUser.roles as UserRole[],
        signingNationalId: editDetail?.signingNationalId,
      });
    } else if (open) {
      form.resetFields();
    }
  }, [open, editUser, editDetail, form]);

  async function handleOk() {
    try {
      const values = await form.validateFields();
      if (isEdit) {
        const body: UpdateUserRequest = {
          fullName: values.fullName,
          department: values.department,
          orgUnitId: values.orgUnitId,
          roles: values.roles,
          signingNationalId: values.signingNationalId,
        };
        await updateMutation.mutateAsync(body);
        appMessage.success(t('page.admin.users.updateSuccess'));
      } else {
        // Step 1: Create user
        const newUser = await createMutation.mutateAsync(values);

        // Step 2: Assign to org unit if selected
        if (values.orgUnitId && values.roleInOrg && newUser) {
          const userId = typeof newUser === 'string' ? newUser : (newUser as { id?: string })?.id;
          if (userId) {
            await assignStaffMutation.mutateAsync({
              userId,
              orgUnitId: values.orgUnitId,
              roleInOrg: values.roleInOrg,
            });
          }
        }
        appMessage.success(t('page.admin.users.createSuccess'));
      }
      onClose();
    } catch {
      // validation or API error — antd form shows inline errors
    }
  }

  return (
    <Modal
      title={isEdit ? t('page.admin.users.editTitle') : t('page.admin.users.createTitle')}
      open={open}
      onOk={handleOk}
      onCancel={onClose}
      okText={isEdit ? t('common.save') : t('page.admin.users.createOkBtn')}
      cancelText={t('common.cancel')}
      confirmLoading={isPending}
      // Block save until the edit detail (incl. CCCD) has loaded SUCCESSFULLY. If the detail fetch
      // is still loading OR errored, the CCCD field is empty for the wrong reason — saving the
      // full-replace PUT would wipe the stored value. (A user deliberately clearing the field on a
      // successfully-loaded form still works — that path has detailError=false.)
      okButtonProps={{ disabled: isEdit && (detailLoading || detailError) }}
      destroyOnHidden
      width={560}
    >
      <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
        {isEdit && detailError && (
          <Alert
            type="error"
            showIcon
            message={t('page.admin.users.detailLoadError')}
            style={{ marginBottom: 12 }}
          />
        )}

        <Form.Item
          name="fullName"
          label={t('page.admin.users.col.fullName')}
          rules={[{ required: true, message: t('page.admin.users.fullNameRequired') }]}
        >
          <Input placeholder={t('page.admin.users.fullNamePlaceholder')} />
        </Form.Item>

        <Form.Item
          name="email"
          label={t('page.admin.users.col.email')}
          rules={[
            { required: true, message: t('page.admin.users.emailRequired') },
            { type: 'email', message: t('page.admin.users.emailInvalid') },
          ]}
        >
          <Input placeholder="example@gov.vn" disabled={isEdit} />
        </Form.Item>

        <Form.Item
          name="roles"
          label={t('page.admin.users.col.roles')}
          rules={[{ required: true, message: t('page.admin.users.rolesRequired') }]}
        >
          <Select mode="multiple" placeholder={t('page.admin.users.rolesPlaceholder')} options={ROLE_OPTIONS} />
        </Form.Item>

        {/* Org unit assignment — TreeSelect shows org hierarchy */}
        <Form.Item name="orgUnitId" label="Đơn vị / Phòng ban">
          <TreeSelect
            treeData={treeData}
            placeholder="Chọn đơn vị"
            allowClear
            showSearch
            treeNodeFilterProp="title"
            treeDefaultExpandAll
          />
        </Form.Item>

        <Form.Item name="roleInOrg" label="Vai trò trong đơn vị">
          <Select
            placeholder="Chọn vai trò"
            options={ROLE_IN_ORG_OPTIONS}
            allowClear
          />
        </Form.Item>

        <Form.Item name="department" label={t('page.admin.users.departmentLabel')}>
          <Input placeholder={t('page.admin.users.departmentPlaceholder')} />
        </Form.Item>

        {/* Signer CCCD — optional CA-side signing identity (Viettel Mysign user_id). 12 digits. */}
        <Form.Item
          name="signingNationalId"
          label={t('page.admin.users.cccdLabel')}
          rules={[{ pattern: /^\d{12}$/, message: t('page.admin.users.cccdInvalid') }]}
        >
          <Input placeholder={t('page.admin.users.cccdPlaceholder')} maxLength={12} />
        </Form.Item>

        {!isEdit && (
          <Form.Item name="temporaryPassword" label={t('page.admin.users.tempPasswordLabel')}>
            <Input.Password placeholder={t('page.admin.users.tempPasswordPlaceholder')} />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
}
