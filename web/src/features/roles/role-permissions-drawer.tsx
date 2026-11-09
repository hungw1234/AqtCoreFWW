import { useEffect, useMemo, useState, useRef } from 'react';
import { Drawer, Collapse, Checkbox, Button, Tag, Space, Spin, Modal, Input } from 'antd';
import { useTranslation } from 'react-i18next';
import { SaveOutlined } from '@ant-design/icons';
import { usePermissions, useRolePermissions, useSyncRolePermissions } from './roles-api';
import type { RoleDefinitionDto, PermissionDto } from './roles-api';
import { appMessage } from '@/core/api/notification-bridge';

const JUSTIFICATION_MAX_LENGTH = 2000;

// Vietnamese display names for the permission-tree module groups. The raw code (e.g. SECURITY) stays
// visible as a monospace tag so it remains greppable/mappable; the label just makes the tree scannable.
// Unmapped codes fall back to the raw code.
const MODULE_LABELS: Record<string, string> = {
  IDENTITY: 'Định danh & Quyền',
  FILES: 'Tài liệu',
  AUDIT: 'Kiểm toán',
  WORKFLOW: 'Quy trình',
  FORMS: 'Biểu mẫu',
  NOTIF: 'Thông báo',
  ORG: 'Tổ chức',
  MASTER: 'Danh mục',
  PLATFORM: 'Nền tảng',
  AI: 'Trợ lý AI',
  COLLAB: 'Cộng tác',
  INTEG: 'Tích hợp',
  REPORT: 'Báo cáo',
  RULES: 'Bộ quy tắc',
  SEARCH: 'Tìm kiếm',
  SYSPARAM: 'Tham số hệ thống',
  SYSTEM: 'Quản trị hệ thống',
  SECURITY: 'Bảo mật',
  COMPLIANCE: 'Tuân thủ',
  BUSINESSPROCESS: 'Quy trình nghiệp vụ',
  SIGNATURE: 'Chữ ký số',
};

interface RolePermissionsDrawerProps {
  open: boolean;
  role: RoleDefinitionDto | null;
  onClose: () => void;
}

// Group permissions by moduleCode for Collapse panels
function groupByModule(permissions: PermissionDto[]) {
  const groups: Record<string, PermissionDto[]> = {};
  for (const p of permissions) {
    (groups[p.moduleCode] ??= []).push(p);
  }
  return groups;
}

export function RolePermissionsDrawer({ open, role, onClose }: RolePermissionsDrawerProps) {
  const { t } = useTranslation();
  const { data: allPermissions = [], isLoading: loadingPerms } = usePermissions();
  const { data: rolePerms = [], isLoading: loadingRolePerms } = useRolePermissions(
    role?.id,
  );
  const syncMutation = useSyncRolePermissions(role?.id ?? '');

  // Local checked state — initialized from role permissions
  const [checkedIds, setCheckedIds] = useState<Set<string>>(new Set());

  useEffect(() => {
    if (rolePerms.length > 0) {
      setCheckedIds(new Set(rolePerms.map((rp) => rp.permissionId)));
    } else if (open) {
      setCheckedIds(new Set());
    }
  }, [rolePerms, open]);

  const grouped = useMemo(() => groupByModule(allPermissions), [allPermissions]);
  const moduleNames = Object.keys(grouped).sort();

  function togglePermission(id: string, checked: boolean) {
    setCheckedIds((prev) => {
      const next = new Set(prev);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  function toggleModule(moduleCode: string, checked: boolean) {
    setCheckedIds((prev) => {
      const next = new Set(prev);
      for (const p of grouped[moduleCode]) {
        if (checked) next.add(p.id);
        else next.delete(p.id);
      }
      return next;
    });
  }

  // Ref to store justification entered in modal
  const justificationRef = useRef<string>('');

  async function handleSave(confirmed = false, justification?: string) {
    try {
      const result = await syncMutation.mutateAsync({
        permissionIds: Array.from(checkedIds),
        confirmed,
        justification,
      });

      if (result?.requiresConfirmation && result.warnings.length > 0) {
        // Reset justification ref for new modal
        justificationRef.current = '';

        Modal.confirm({
          title: t('roles.sodWarning'),
          content: (
            <div>
              <ul style={{ maxHeight: 150, overflow: 'auto', marginBottom: 16 }}>
                {result.warnings.map((w, i) => (
                  <li key={i}>{w}</li>
                ))}
              </ul>
              <div style={{ marginBottom: 8 }}>
                <label>{t('roles.sodOverride.justificationOptional')}</label>
              </div>
              <Input.TextArea
                maxLength={JUSTIFICATION_MAX_LENGTH}
                placeholder={t('roles.sodOverride.justificationHint')}
                rows={3}
                showCount
                onChange={(e) => { justificationRef.current = e.target.value; }}
              />
            </div>
          ),
          okText: t('roles.confirmAssign'),
          cancelText: t('common.cancel'),
          onOk: () => handleSave(true, justificationRef.current || undefined),
        });
        return;
      }

      appMessage.success(t('roles.permSaved'));
    } catch {
      appMessage.error(t('roles.permSaveFailed'));
    }
  }

  const isLoading = loadingPerms || loadingRolePerms;

  const collapseItems = moduleNames.map((moduleCode) => {
    const perms = grouped[moduleCode];
    const allChecked = perms.every((p) => checkedIds.has(p.id));
    const someChecked = perms.some((p) => checkedIds.has(p.id));

    return {
      key: moduleCode,
      label: (
        <Space>
          <Checkbox
            checked={allChecked}
            indeterminate={someChecked && !allChecked}
            onChange={(e) => {
              e.stopPropagation();
              toggleModule(moduleCode, e.target.checked);
            }}
            onClick={(e) => e.stopPropagation()}
          />
          <strong>{MODULE_LABELS[moduleCode] ?? moduleCode}</strong>
          <Tag style={{ fontFamily: 'monospace', fontSize: 11 }}>{moduleCode}</Tag>
          <Tag>{perms.length}</Tag>
        </Space>
      ),
      children: (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10, paddingLeft: 24 }}>
          {perms.map((p) => (
            <Checkbox
              key={p.id}
              checked={checkedIds.has(p.id)}
              onChange={(e) => togglePermission(p.id, e.target.checked)}
            >
              <Space size={6}>
                <Tag style={{ fontFamily: 'monospace', fontSize: 12 }}>
                  {p.resourceCode}.{p.actionCode}
                </Tag>
                <span>{p.name}</span>
                {p.isSensitive && <Tag color="orange">{t('roles.sensitive')}</Tag>}
              </Space>
            </Checkbox>
          ))}
        </div>
      ),
    };
  });

  return (
    <Drawer
      title={t('roles.permTitle', { name: role?.name ?? '' })}
      open={open}
      onClose={onClose}
      width={560}
      destroyOnClose
      extra={
        <Button
          type="primary"
          icon={<SaveOutlined />}
          loading={syncMutation.isPending}
          onClick={() => handleSave()}
        >
          {t('common.save')}
        </Button>
      }
    >
      {isLoading ? (
        <div style={{ textAlign: 'center', padding: 40 }}>
          <Spin />
        </div>
      ) : (
        <Collapse items={collapseItems} defaultActiveKey={moduleNames.slice(0, 2)} />
      )}
    </Drawer>
  );
}
