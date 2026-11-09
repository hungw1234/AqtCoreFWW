/**
 * signature-config-page.tsx — Admin UI for tenant digital-signature configuration.
 * Route: /admin/signature-config
 * Requires: SECURITY.SIGNATURE_CONFIG.VIEW permission (SystemAdmin-only).
 *
 * tenantId: resolved from sessionStorage 'admin-tenant-override' (AdminTenantSelector).
 * The admin controller requires {tenantId:guid} in the route — this page is only
 * reachable/useful when a tenant override is active.
 */

import { useState } from 'react';
import { Descriptions, Button, Tag, Space, Skeleton, Alert } from 'antd';
import { EditOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useSignatureConfig,
  useAvailableProviders,
  useUpdateSignatureConfig,
} from './signature-api';
import { SignatureConfigFormModal } from './signature-config-form-modal';
import { hasCapability } from './signature-types';
import type { UpdateSignatureConfigRequest, AvailableProviderDto } from './signature-types';

// ─── Capability flag display ──────────────────────────────────────────────────

/**
 * Parse comma-separated capability string (JsonStringEnumConverter + [Flags])
 * and display human-readable tags.
 * Example input: "ServerSideSigning, RequiresLocalAgent"
 */
function CapabilityTags({ capabilities }: { capabilities: string }) {
  const { t } = useTranslation();
  const tags: React.ReactNode[] = [];

  if (hasCapability(capabilities, 'ServerSideSigning'))
    tags.push(<Tag key="server" color="blue">{t('digitalSignature.capServerSide', 'Ký phía máy chủ')}</Tag>);
  if (hasCapability(capabilities, 'ClientSideSigning'))
    tags.push(<Tag key="client" color="cyan">{t('digitalSignature.capClientSide', 'Ký phía client')}</Tag>);
  if (hasCapability(capabilities, 'BatchSigning'))
    tags.push(<Tag key="batch" color="purple">{t('digitalSignature.capBatch', 'Ký hàng loạt')}</Tag>);
  if (hasCapability(capabilities, 'LongTermValidation'))
    tags.push(<Tag key="ltv" color="geekblue">{t('digitalSignature.capLtv', 'LTV')}</Tag>);
  if (hasCapability(capabilities, 'OcspStapling'))
    tags.push(<Tag key="ocsp" color="green">{t('digitalSignature.capOcspStapling', 'OCSP Stapling')}</Tag>);
  if (hasCapability(capabilities, 'RequiresLocalAgent'))
    tags.push(<Tag key="agent" color="orange">{t('digitalSignature.capLocalAgent', 'Cần agent cục bộ')}</Tag>);
  if (tags.length === 0 || hasCapability(capabilities, 'None'))
    tags.push(<Tag key="none" color="default">{t('digitalSignature.capNone', 'Không có')}</Tag>);

  return <Space wrap>{tags}</Space>;
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * SignatureConfigPage — displays and edits tenant-level digital-signature settings.
 * Requires active tenant override in sessionStorage (SystemAdmin only).
 */
export function SignatureConfigPage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);

  // Read the active tenant override set by AdminTenantSelector
  const tenantId = sessionStorage.getItem('admin-tenant-override') ?? '';

  const { data: config, isLoading: configLoading, error: configError } = useSignatureConfig(tenantId);
  const { data: providers = [], isLoading: providersLoading } = useAvailableProviders();
  const updateConfig = useUpdateSignatureConfig(tenantId);

  async function handleSave(req: UpdateSignatureConfigRequest) {
    try {
      await updateConfig.mutateAsync(req);
      appMessage.success(t('common.operationSuccess', 'Thao tác thành công'));
      setModalOpen(false);
    } catch {
      appMessage.error(t('common.operationFailed', 'Thao tác thất bại'));
    }
  }

  const isLoading = configLoading || providersLoading;

  // ── Render ─────────────────────────────────────────────────────────────────

  if (!tenantId) {
    return (
      <div>
        <AdminPageHeader title={t('digitalSignature.configPageTitle', 'Cấu hình chữ ký số')} />
        <AdminContentCard>
          <Alert
            type="warning"
            showIcon
            message={t(
              'digitalSignature.noTenantSelected',
              'Vui lòng chọn tenant từ thanh điều hướng trước khi quản lý cấu hình chữ ký số.',
            )}
          />
        </AdminContentCard>
      </div>
    );
  }

  return (
    <div>
      <AdminPageHeader
        title={t('digitalSignature.configPageTitle', 'Cấu hình chữ ký số')}
        actions={
          <Button
            type="primary"
            icon={<EditOutlined />}
            onClick={() => setModalOpen(true)}
            disabled={isLoading || !!configError}
          >
            {t('common.edit', 'Sửa')}
          </Button>
        }
      />

      <AdminContentCard>
        {configError && (
          <Alert
            type="error"
            showIcon
            message={t('digitalSignature.loadError', 'Không thể tải cấu hình chữ ký số')}
            style={{ marginBottom: 16 }}
          />
        )}

        {isLoading ? (
          <Skeleton active paragraph={{ rows: 5 }} />
        ) : config ? (
          <Descriptions
            bordered
            column={1}
            size="small"
            labelStyle={{ width: 220, fontWeight: 500 }}
          >
            <Descriptions.Item label={t('digitalSignature.isEnabled', 'Kích hoạt')}>
              {config.isEnabled
                ? <Tag color="green" icon={<SafetyCertificateOutlined />}>{t('common.active', 'Đang bật')}</Tag>
                : <Tag color="default">{t('common.inactive', 'Đang tắt')}</Tag>}
            </Descriptions.Item>

            <Descriptions.Item label={t('digitalSignature.providerKey', 'Nhà cung cấp đang dùng')}>
              {config.providerKey
                ? <Tag color="blue">{config.providerKey}</Tag>
                : <span style={{ color: '#aaa' }}>—</span>}
            </Descriptions.Item>

            <Descriptions.Item label={t('digitalSignature.ocspMode', 'Chế độ OCSP')}>
              <Tag>{config.ocspMode}</Tag>
            </Descriptions.Item>

            <Descriptions.Item label={t('digitalSignature.allowedCaRootThumbprints', 'Root CA thumbprints')}>
              <span style={{ fontFamily: 'monospace', fontSize: 11, wordBreak: 'break-all' }}>
                {config.allowedCaRootThumbprints || <span style={{ color: '#aaa' }}>—</span>}
              </span>
            </Descriptions.Item>

            <Descriptions.Item label={t('digitalSignature.availableProviders', 'Nhà cung cấp khả dụng')}>
              <Space wrap>
                {providers.length === 0
                  ? <span style={{ color: '#aaa' }}>—</span>
                  : providers.map((p: AvailableProviderDto) => (
                      <Space key={p.providerKey} direction="vertical" size={2}>
                        <Tag color="geekblue">{p.displayName || p.providerKey}</Tag>
                        <CapabilityTags capabilities={p.capabilities} />
                      </Space>
                    ))}
              </Space>
            </Descriptions.Item>

            <Descriptions.Item label={t('digitalSignature.updatedAt', 'Cập nhật lần cuối')}>
              {config.updatedAt
                ? new Date(config.updatedAt).toLocaleString('vi-VN')
                : '—'}
            </Descriptions.Item>
          </Descriptions>
        ) : (
          <Alert
            type="info"
            showIcon
            message={t('digitalSignature.notConfigured', 'Tenant này chưa cấu hình chữ ký số. Nhấn Sửa để thiết lập.')}
          />
        )}
      </AdminContentCard>

      <SignatureConfigFormModal
        open={modalOpen}
        config={config ?? null}
        providers={providers}
        isSubmitting={updateConfig.isPending}
        onSave={handleSave}
        onCancel={() => setModalOpen(false)}
      />
    </div>
  );
}
