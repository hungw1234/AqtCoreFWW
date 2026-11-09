import { Button, Space, Tabs, Alert } from 'antd';
import { AuditOutlined, DownloadOutlined, LoginOutlined, SafetyOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useExportAuditLogs, useVerifyAuditChain } from './audit-api';
import { AuditLogTable } from './audit-log-table';
import { LoginAuditTable } from './login-audit-table';
import { SecurityIncidentsTable } from './security-incidents-table';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

// AuditLogPage — tabbed audit viewer: audit logs | login history | security incidents
export function AuditLogPage() {
  const { t } = useTranslation();
  const { mutate: exportCsv, isPending: isExporting } = useExportAuditLogs();
  const { mutate: verifyChain, isPending: isVerifying, data: verifyResult, reset: resetVerify } = useVerifyAuditChain();

  const TAB_ITEMS = [
    {
      key: 'logs',
      label: (
        <span>
          <AuditOutlined /> {t('page.audit.tab.logs')}
        </span>
      ),
      children: <AuditLogTable />,
    },
    {
      key: 'login',
      label: (
        <span>
          <LoginOutlined /> {t('page.audit.tab.login')}
        </span>
      ),
      children: <LoginAuditTable />,
    },
    {
      key: 'incidents',
      label: (
        <span>
          <SafetyOutlined /> {t('page.audit.tab.incidents')}
        </span>
      ),
      children: <SecurityIncidentsTable />,
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('page.audit.title')}
        icon={<AuditOutlined />}
        actions={
          <Space>
            <Button
              icon={<SafetyCertificateOutlined />}
              loading={isVerifying}
              onClick={() => { resetVerify(); verifyChain(); }}
            >
              Xác minh chuỗi HMAC
            </Button>
            <Button
              icon={<DownloadOutlined />}
              loading={isExporting}
              onClick={() => exportCsv({})}
            >
              {t('page.audit.exportCsv', 'Export CSV')}
            </Button>
          </Space>
        }
      />

      {/* HMAC chain verification result banner */}
      {verifyResult && (
        <Alert
          style={{ marginBottom: 16 }}
          type={verifyResult.isValid ? 'success' : 'error'}
          showIcon
          closable
          onClose={resetVerify}
          message={
            verifyResult.isValid
              ? 'Chuỗi HMAC hợp lệ — toàn bộ nhật ký chưa bị giả mạo'
              : `Chuỗi HMAC bị phá vỡ tại: ${verifyResult.brokenAt ?? 'không xác định'}`
          }
        />
      )}
      <AdminContentCard>
        <Tabs defaultActiveKey="logs" items={TAB_ITEMS} destroyOnHidden={false} />
      </AdminContentCard>
    </div>
  );
}
