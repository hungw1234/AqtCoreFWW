import { useState } from 'react';
import { Table, Tag, Button, Space, Tooltip, Typography } from 'antd';
import { DownloadOutlined } from '@ant-design/icons';
import type { ColumnsType, TableProps } from 'antd/es/table';
import { useAuditLogs } from './audit-api';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { AuditLogDetailDrawer } from './audit-log-detail-drawer';
import { AuditLogFilters, type AuditLogFilterValues } from './audit-log-filters';
import { exportAuditLogsToCsv } from './audit-log-export';
import type { AuditLogEntry } from './audit-types';

const { Text } = Typography;

// Format ISO timestamp → dd/MM/yyyy HH:mm:ss (Vietnamese locale)
function formatTs(iso: string): string {
  return new Date(iso).toLocaleString('vi-VN', {
    day: '2-digit', month: '2-digit', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit',
    hour12: false,
  });
}

// Action → tag color map
const ACTION_COLORS: Record<string, string> = {
  Create: 'green', Update: 'blue', Delete: 'red',
  Login: 'cyan', Logout: 'default', Export: 'purple', Import: 'orange',
};

const COLUMNS: ColumnsType<AuditLogEntry> = [
  {
    title: 'Thời gian',
    dataIndex: 'occurredAt',
    key: 'occurredAt',
    width: 160,
    sorter: true,
    defaultSortOrder: 'descend',
    render: (v: string) => <Text style={{ fontSize: 12 }}>{v ? formatTs(v) : '—'}</Text>,
  },
  {
    title: 'Người dùng',
    dataIndex: 'userName',
    key: 'userName',
    width: 160,
    sorter: true,
    ellipsis: true,
  },
  {
    title: 'Hành động',
    dataIndex: 'action',
    key: 'action',
    width: 160,
    ellipsis: { showTitle: false },
    render: (v: string) => (
      <Tooltip title={v} placement="topLeft">
        <Tag color={ACTION_COLORS[v] ?? 'default'} style={{ maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {v}
        </Tag>
      </Tooltip>
    ),
  },
  {
    title: 'Module',
    dataIndex: 'moduleName',
    key: 'moduleName',
    width: 140,
    ellipsis: true,
  },
  {
    title: 'Loại tài nguyên',
    dataIndex: 'resourceType',
    key: 'resourceType',
    width: 130,
    ellipsis: true,
  },
  {
    title: 'ID tài nguyên',
    dataIndex: 'resourceId',
    key: 'resourceId',
    width: 130,
    ellipsis: true,
    responsive: ['md'],
    render: (v?: string) => v ? (
      <Tooltip title={v}>
        <Text code style={{ fontSize: 11 }}>{v.slice(0, 8)}…</Text>
      </Tooltip>
    ) : <Text type="secondary">—</Text>,
  },
  {
    title: 'Địa chỉ IP',
    dataIndex: 'ipAddress',
    key: 'ipAddress',
    width: 120,
    responsive: ['lg'],
    render: (v: string) => <Text code style={{ fontSize: 12 }}>{v}</Text>,
  },
];

// AuditLogTable — server-paginated, filterable, sortable audit log table
export function AuditLogTable() {
  const [filters, setFilters] = useState<AuditLogFilterValues>({});
  const [selected, setSelected] = useState<AuditLogEntry | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);

  const { antPagination, toQueryParams } = useServerPagination(20, [filters]);

  const queryParams = { ...toQueryParams(), ...filters };
  const { data, isFetching } = useAuditLogs({
    page: queryParams.pageNumber,
    pageSize: queryParams.pageSize,
    ...filters,
  });

  const entries = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  function handleRowClick(record: AuditLogEntry) {
    setSelected(record);
    setDrawerOpen(true);
  }

  // Wire Ant Table onChange for sort — updates filters to trigger re-fetch
  const handleTableChange: TableProps<AuditLogEntry>['onChange'] = (_pg, _filters, sorter) => {
    const s = Array.isArray(sorter) ? sorter[0] : sorter;
    if (s?.field && s?.order) {
      setFilters((prev) => ({
        ...prev,
        sortBy: String(s.field),
        sortOrder: s.order === 'ascend' ? 'asc' : 'desc',
      } as AuditLogFilterValues));
    }
  };

  return (
    <>
      <Space direction="vertical" style={{ width: '100%' }} size={0}>
        {/* Filter bar */}
        <AuditLogFilters
          values={filters}
          onChange={setFilters}
          onReset={() => setFilters({})}
        />

        {/* Export button */}
        <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 8 }}>
          <Button
            icon={<DownloadOutlined />}
            disabled={entries.length === 0}
            onClick={() => exportAuditLogsToCsv(entries)}
          >
            Xuất CSV
          </Button>
        </div>

        {/* Data table */}
        <Table<AuditLogEntry>
          rowKey="id"
          columns={COLUMNS}
          dataSource={entries}
          loading={isFetching}
          size="small"
          scroll={{ x: 900 }}
          pagination={{ ...antPagination, total }}
          onChange={handleTableChange}
          onRow={(record) => ({ onClick: () => handleRowClick(record), style: { cursor: 'pointer' } })}
        />
      </Space>

      <AuditLogDetailDrawer
        entry={selected}
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
      />
    </>
  );
}
