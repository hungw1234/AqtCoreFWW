import { Drawer, Descriptions, Typography, Tag, Divider, Alert } from 'antd';
import { useTranslation } from 'react-i18next';
import type { AuditLogEntry } from './audit-types';

const { Text, Paragraph } = Typography;

interface AuditLogDetailDrawerProps {
  entry: AuditLogEntry | null;
  open: boolean;
  onClose: () => void;
}

// Format ISO timestamp → dd/MM/yyyy HH:mm:ss
function formatTimestamp(iso: string): string {
  return new Date(iso).toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  });
}

// Pretty-print JSON string; return raw string on parse failure
function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

// Map action to Ant Design Tag color
function actionColor(action: string): string {
  const map: Record<string, string> = {
    Create: 'green',
    Update: 'blue',
    Delete: 'red',
    Login: 'cyan',
    Logout: 'default',
    Export: 'purple',
    Import: 'orange',
  };
  return map[action] ?? 'default';
}

// AuditLogDetailDrawer — shows all fields of a selected audit log entry
export function AuditLogDetailDrawer({ entry, open, onClose }: AuditLogDetailDrawerProps) {
  const { t } = useTranslation();

  return (
    <Drawer
      title={t('page.audit.detail.title')}
      open={open}
      onClose={onClose}
      width={560}
      destroyOnHidden
    >
      {entry && (
        <>
          <Descriptions column={1} size="small" bordered>
            <Descriptions.Item label={t('page.audit.detail.timestamp')}>
              {formatTimestamp(entry.occurredAt)}
            </Descriptions.Item>
            <Descriptions.Item label={t('page.audit.detail.userName')}>
              {entry.userName}
            </Descriptions.Item>
            <Descriptions.Item label={t('page.audit.detail.userId')}>
              <Text copyable code>{entry.userId}</Text>
            </Descriptions.Item>
            <Descriptions.Item label={t('page.audit.detail.action')}>
              <Tag color={actionColor(entry.action)}>{entry.action}</Tag>
            </Descriptions.Item>
            <Descriptions.Item label={t('page.audit.detail.moduleName')}>{entry.moduleName}</Descriptions.Item>
            <Descriptions.Item label={t('page.audit.detail.resourceType')}>{entry.resourceType}</Descriptions.Item>
            <Descriptions.Item label={t('page.audit.detail.resourceId')}>
              <Text copyable code>{entry.resourceId}</Text>
            </Descriptions.Item>
            <Descriptions.Item label={t('page.audit.detail.ipAddress')}>
              <Text code>{entry.ipAddress}</Text>
            </Descriptions.Item>
            <Descriptions.Item label={t('page.audit.detail.tenantId')}>
              <Text copyable code>{entry.tenantId}</Text>
            </Descriptions.Item>
            {entry.correlationId && (
              <Descriptions.Item label={t('page.audit.detail.correlationId')}>
                <Text copyable code>{entry.correlationId}</Text>
              </Descriptions.Item>
            )}
          </Descriptions>

          {entry.details && (
            <>
              <Divider orientation="left" plain>
                {t('page.audit.detail.changesSection')}
              </Divider>
              <Paragraph>
                <pre
                  style={{
                    background: '#f5f5f5',
                    padding: 12,
                    borderRadius: 4,
                    fontSize: 12,
                    maxHeight: 320,
                    overflow: 'auto',
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-all',
                  }}
                >
                  {prettyJson(entry.details)}
                </pre>
              </Paragraph>
            </>
          )}

          {!entry.details && (
            <Alert
              style={{ marginTop: 16 }}
              type="info"
              message={t('page.audit.detail.noChanges')}
              showIcon
            />
          )}
        </>
      )}
    </Drawer>
  );
}
