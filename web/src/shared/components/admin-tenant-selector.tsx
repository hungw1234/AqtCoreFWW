// AdminTenantSelector — cross-tenant override control for SystemAdmin users
// Stores the selected tenant ID in sessionStorage as 'admin-tenant-override'.
// The api-client interceptor picks up this value and sends it as X-Tenant-Id header.

import { useState, useEffect } from 'react';
import { Button, Select, Space, Tooltip, Tag } from 'antd';
import { SwapOutlined, CloseCircleOutlined } from '@ant-design/icons';
import { useQueryClient } from '@tanstack/react-query';
import { usePermissions } from '@/features/auth/use-permissions';
import { apiClient, shouldInvalidateOnTenantSwitch } from '@/core/api';
import { appMessage } from '@/core/api/notification-bridge';

const STORAGE_KEY = 'admin-tenant-override';

interface TenantSummary {
  tenantId: string;
  tenantName: string | null;
  userCount: number;
}

/**
 * True when the current user has platform-tier admin permission.
 * SYSTEM.CONFIG.MANAGE is the SystemAdmin-only proxy per RolePermissions seeder
 * excluded list — Admin role is denied this perm. Cross-tenant override is a
 * platform-level operation, so this is the right gate.
 */
function useIsSystemAdmin(): boolean {
  const { hasPermission } = usePermissions();
  return hasPermission('SYSTEM.CONFIG.MANAGE');
}

/**
 * AdminTenantSelector — only renders for SystemAdmin role users.
 * Shows a Select dropdown to pick a tenant from existing tenants for cross-tenant API calls.
 * Displays an active indicator badge when an override is active.
 */
export function AdminTenantSelector() {
  const isSystemAdmin = useIsSystemAdmin();
  const queryClient = useQueryClient();
  const [selectorVisible, setSelectorVisible] = useState(false);
  const [tenants, setTenants] = useState<TenantSummary[]>([]);
  const [loading, setLoading] = useState(false);
  const [activeOverride, setActiveOverride] = useState<string | null>(() =>
    sessionStorage.getItem(STORAGE_KEY)
  );

  // Reload tenant-scoped data after override changes. Cancel in-flight first so
  // pre-switch responses can't write into the post-switch cache key (race fix).
  // Auth/permissions/menu/i18n/session caches are preserved via predicate.
  async function reloadAfterTenantChange() {
    await queryClient.cancelQueries({ predicate: shouldInvalidateOnTenantSwitch });
    await queryClient.invalidateQueries({
      predicate: shouldInvalidateOnTenantSwitch,
      refetchType: 'active',
    });
  }

  // Sync local state and auto-fetch tenants when override is active (for tag/tooltip display)
  useEffect(() => {
    const stored = sessionStorage.getItem(STORAGE_KEY);
    setActiveOverride(stored);
    if (stored) fetchTenants();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (!isSystemAdmin) return null;

  // Fetch tenant list from BE
  async function fetchTenants() {
    if (tenants.length > 0) return; // already loaded
    setLoading(true);
    try {
      const res = await apiClient.get<TenantSummary[]>('/admin/users/tenants');
      setTenants(res.data ?? []);
    } catch (err) {
      // Surface server detail (status + errorCode + correlationId) to console for ops debugging
      // — toast giữ generic để không leak thông tin BE cho user.
      console.error('[AdminTenantSelector] fetch tenants failed:', err);
      appMessage.error('Không thể tải danh sách tenant');
    } finally {
      setLoading(false);
    }
  }

  async function handleSelect(tenantId: string) {
    sessionStorage.setItem(STORAGE_KEY, tenantId);
    setActiveOverride(tenantId);
    setSelectorVisible(false);
    await reloadAfterTenantChange();
    appMessage.success('Đã chuyển tenant — đang tải lại dữ liệu');
  }

  async function handleClear() {
    sessionStorage.removeItem(STORAGE_KEY);
    setActiveOverride(null);
    setSelectorVisible(false);
    await reloadAfterTenantChange();
    appMessage.success('Đã thoát tenant — quay về dữ liệu mặc định');
  }

  // Truncate GUID for compact display: first 8 chars
  function shortId(id: string) {
    return id.length > 8 ? `${id.substring(0, 8)}…` : id;
  }

  return (
    <Space size={4} align="center">
      {activeOverride && (
        <Tooltip title={`Đang sử dụng tenant: ${tenants.find((t) => t.tenantId === activeOverride)?.tenantName ?? activeOverride}`}>
          <Tag
            color="orange"
            closable
            onClose={() => { void handleClear(); }}
            icon={<SwapOutlined />}
            style={{ fontSize: 11, lineHeight: '18px', cursor: 'default' }}
          >
            {tenants.find((t) => t.tenantId === activeOverride)?.tenantName
              ?? `Tenant: ${shortId(activeOverride)}`}
          </Tag>
        </Tooltip>
      )}

      {selectorVisible ? (
        <Space size={4}>
          <Select
            size="small"
            placeholder="Chọn tenant"
            loading={loading}
            value={activeOverride ?? undefined}
            onChange={(v) => { void handleSelect(v); }}
            onDropdownVisibleChange={(open) => { if (open) fetchTenants(); }}
            style={{ width: 280, fontSize: 12 }}
            showSearch
            optionFilterProp="label"
            options={tenants.map((t) => ({
              value: t.tenantId,
              label: t.tenantName
                ? `${t.tenantName} (${t.userCount} người dùng)`
                : `${shortId(t.tenantId)} (${t.userCount} người dùng)`,
            }))}
            notFoundContent={loading ? 'Đang tải...' : 'Không có tenant'}
          />
          <Button
            size="small"
            type="text"
            icon={<CloseCircleOutlined style={{ fontSize: 12 }} />}
            onClick={() => setSelectorVisible(false)}
            style={{ width: 24, height: 24, minWidth: 24, padding: 0, color: 'var(--gov-text-secondary)' }}
          />
        </Space>
      ) : (
        <Tooltip title="Chuyển đổi tenant (chỉ SystemAdmin)">
          <Button
            size="small"
            type="text"
            icon={<SwapOutlined />}
            onClick={() => { setSelectorVisible(true); fetchTenants(); }}
            style={{ color: 'var(--gov-text-secondary)', fontSize: 12 }}
          >
            {activeOverride ? 'Đổi tenant' : 'Chuyển tenant'}
          </Button>
        </Tooltip>
      )}
    </Space>
  );
}
