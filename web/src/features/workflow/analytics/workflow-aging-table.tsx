// Aging report table — instances stuck in a state longer than threshold
import { Card, Table, Spin, Alert, InputNumber, Space } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { memo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useAgingReport } from '../workflow-analytics-api';

export const AgingTable = memo(function AgingTable() {
  const { t } = useTranslation();
  const [minHours, setMinHours] = useState(24);
  const { data, isLoading, error } = useAgingReport(minHours);

  const columns: ColumnsType<{
    instanceId: string;
    definitionId: string;
    stateName: string;
    hoursInState: number;
    enteredAt: string;
  }> = [
    {
      title: t('workflow.analytics.aging.instanceId'),
      dataIndex: 'instanceId',
      ellipsis: true,
      width: 160,
      render: (v: string) => <span style={{ fontFamily: 'monospace', fontSize: 11 }}>{v}</span>,
    },
    {
      title: t('workflow.analytics.aging.definitionId'),
      dataIndex: 'definitionId',
      ellipsis: true,
      width: 160,
      render: (v: string) => <span style={{ fontFamily: 'monospace', fontSize: 11 }}>{v}</span>,
    },
    {
      title: t('workflow.analytics.aging.stateName'),
      dataIndex: 'stateName',
      ellipsis: true,
    },
    {
      title: t('workflow.analytics.aging.hoursInState'),
      dataIndex: 'hoursInState',
      width: 110,
      align: 'right',
      render: (v: number) => `${v.toFixed(1)}h`,
      sorter: (a, b) => a.hoursInState - b.hoursInState,
      defaultSortOrder: 'descend',
    },
    {
      title: t('workflow.analytics.aging.enteredAt'),
      dataIndex: 'enteredAt',
      width: 130,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
  ];

  return (
    <Card
      title={t('workflow.analytics.aging.title')}
      variant="borderless"
      className="gov-card-hover"
      style={{ boxShadow: 'var(--elevation-1)' }}
      extra={
        <Space>
          <span style={{ fontSize: 12, color: '#888' }}>{t('workflow.analytics.aging.minHoursLabel')}</span>
          <InputNumber
            min={1}
            max={720}
            value={minHours}
            onChange={(v) => setMinHours(v ?? 24)}
            size="small"
            style={{ width: 64 }}
          />
        </Space>
      }
    >
      {error ? (
        <Alert type="error" message={t('workflow.analytics.loadError')} showIcon />
      ) : isLoading ? (
        <Spin style={{ display: 'block', textAlign: 'center', padding: 48 }} />
      ) : (
        <Table
          rowKey="instanceId"
          columns={columns}
          dataSource={data ?? []}
          size="small"
          scroll={{ x: 720 }}
          pagination={{ pageSize: 10, showSizeChanger: false }}
          locale={{ emptyText: t('workflow.analytics.noData') }}
        />
      )}
    </Card>
  );
});
