import { Table, Tag, Typography, Badge } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { useSecurityIncidents } from './audit-api';
import type { SecurityIncidentEntry, SecurityIncidentParams } from './audit-types';

const { Text } = Typography;

function formatTs(iso: string): string {
  return new Date(iso).toLocaleString('vi-VN', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
    hour12: false,
  });
}

const SEVERITY_COLORS: Record<string, string> = {
  Low: 'default', Medium: 'orange', High: 'red', Critical: 'magenta',
};

const STATUS_BADGE: Record<string, 'default' | 'processing' | 'error' | 'success' | 'warning'> = {
  Open: 'error',
  Investigating: 'processing',
  Resolved: 'success',
  Closed: 'default',
};

// SecurityIncidentsTable — security incident log wired to GET /api/v1/admin/incidents
export function SecurityIncidentsTable() {
  const { t } = useTranslation();
  const { antPagination, toQueryParams } = useServerPagination(20);
  const params: SecurityIncidentParams = toQueryParams();
  const { data, isFetching } = useSecurityIncidents(params);

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  const COLUMNS: ColumnsType<SecurityIncidentEntry> = [
    {
      title: t('audit.col.timestamp'),
      dataIndex: 'occurredAt',
      key: 'occurredAt',
      width: 160,
      defaultSortOrder: 'descend',
      sorter: true,
      render: (v: string) => <Text style={{ fontSize: 12 }}>{v ? formatTs(v) : '—'}</Text>,
    },
    {
      title: t('audit.col.incidentType'),
      dataIndex: 'type',
      key: 'type',
      width: 160,
      ellipsis: true,
    },
    {
      title: t('audit.col.severity'),
      dataIndex: 'severity',
      key: 'severity',
      width: 100,
      render: (v: string) => <Tag color={SEVERITY_COLORS[v] ?? 'default'}>{v}</Tag>,
    },
    {
      title: t('audit.col.description'),
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
    },
    {
      title: t('audit.col.status'),
      dataIndex: 'status',
      key: 'status',
      width: 140,
      render: (v: string) => (
        <Badge
          status={STATUS_BADGE[v] ?? 'default'}
          text={t(`audit.incidentStatus.${v}`, { defaultValue: v })}
        />
      ),
    },
    {
      title: t('audit.col.ipAddress'),
      dataIndex: 'ipAddress',
      key: 'ipAddress',
      width: 120,
      render: (v?: string) => v
        ? <Text code style={{ fontSize: 12 }}>{v}</Text>
        : <Text type="secondary">—</Text>,
    },
  ];

  return (
    <Table<SecurityIncidentEntry>
      rowKey="id"
      columns={COLUMNS}
      dataSource={items}
      loading={isFetching}
      size="small"
      scroll={{ x: 800 }}
      pagination={{ ...antPagination, total }}
      locale={{ emptyText: t('audit.emptyIncidents') }}
    />
  );
}
