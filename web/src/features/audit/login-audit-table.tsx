import { Table, Tag, Typography } from 'antd';
import type { ColumnsType, TableProps } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { useLoginAudit } from './audit-api';
import type { LoginAuditEntry, LoginAuditParams } from './audit-types';

const { Text } = Typography;

function formatTs(iso: string): string {
  return new Date(iso).toLocaleString('vi-VN', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
    hour12: false,
  });
}

// LoginAuditTable — login attempt history wired to GET /api/v1/admin/login-audit
export function LoginAuditTable() {
  const { t } = useTranslation();
  const { antPagination, toQueryParams } = useServerPagination(20);
  const params: LoginAuditParams = toQueryParams();
  const { data, isFetching } = useLoginAudit(params);

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  const COLUMNS: ColumnsType<LoginAuditEntry> = [
    {
      title: t('audit.col.timestamp'),
      dataIndex: 'attemptedAt',
      key: 'attemptedAt',
      width: 160,
      defaultSortOrder: 'descend',
      sorter: true,
      render: (v: string) => <Text style={{ fontSize: 12 }}>{v ? formatTs(v) : '—'}</Text>,
    },
    {
      title: t('audit.col.user'),
      dataIndex: 'email',
      key: 'email',
      width: 160,
      ellipsis: true,
    },
    {
      title: t('audit.col.ipAddress'),
      dataIndex: 'ipAddress',
      key: 'ipAddress',
      width: 130,
      render: (v: string) => <Text code style={{ fontSize: 12 }}>{v}</Text>,
    },
    {
      title: t('audit.col.result'),
      dataIndex: 'success',
      key: 'success',
      width: 100,
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'red'}>
          {v ? t('audit.loginSuccess') : t('audit.loginFail')}
        </Tag>
      ),
    },
    {
      title: t('audit.col.failureReason'),
      dataIndex: 'failureReason',
      key: 'failureReason',
      ellipsis: true,
      render: (v?: string) => v ? <Text type="danger">{v}</Text> : <Text type="secondary">—</Text>,
    },
    {
      title: t('audit.col.userAgent'),
      dataIndex: 'userAgent',
      key: 'userAgent',
      ellipsis: true,
      render: (v: string) => (
        <Text style={{ fontSize: 11 }} type="secondary">{v}</Text>
      ),
    },
  ];

  const handleChange: TableProps<LoginAuditEntry>['onChange'] = (_pagination, _filters, sorter) => {
    // sorter handling could be wired to sort params if the API supports it
    void sorter;
  };

  return (
    <Table<LoginAuditEntry>
      rowKey="id"
      columns={COLUMNS}
      dataSource={items}
      loading={isFetching}
      size="small"
      scroll={{ x: 800 }}
      pagination={{ ...antPagination, total }}
      locale={{ emptyText: t('audit.emptyLogin') }}
      onChange={handleChange}
    />
  );
}
