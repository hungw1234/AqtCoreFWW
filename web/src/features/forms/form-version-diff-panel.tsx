// Version diff panel — compare fields between two template versions
import { useState } from 'react';
import { InputNumber, Button, Table, Tag, Space, Spin, Alert } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import type { DiffFieldDto } from './form-types';
import { useFormVersionDiff } from './form-api';

interface Props {
  templateId: string;
  currentVersion: number;
}

export function FormVersionDiffPanel({ templateId, currentVersion }: Props) {
  const [fromVersion, setFromVersion] = useState(1);
  const [toVersion, setToVersion] = useState(currentVersion);
  const [enabled, setEnabled] = useState(false);

  const { data, isLoading, isError } = useFormVersionDiff(
    templateId, fromVersion, toVersion, enabled
  );

  const cols: ColumnsType<DiffFieldDto & { change: string }> = [
    { title: 'Field Key', dataIndex: 'fieldKey', key: 'fieldKey' },
    { title: 'Label (VI)', dataIndex: 'labelVi', key: 'labelVi' },
    { title: 'Added In', dataIndex: 'addedInVersion', key: 'addedInVersion', width: 90 },
    {
      title: 'Change',
      dataIndex: 'change',
      key: 'change',
      width: 90,
      render: (v: string) => {
        const color = v === 'Added' ? 'green' : v === 'Removed' ? 'red' : 'orange';
        return <Tag color={color}>{v}</Tag>;
      },
    },
  ];

  type DiffRow = DiffFieldDto & { change: string };
  const rows: DiffRow[] = data
    ? [
        ...data.added.map((f) => ({ ...f, change: 'Added' as const })),
        ...data.removed.map((f) => ({ ...f, change: 'Removed' as const })),
        ...data.modified.map((f) => ({ ...f, change: 'Modified' as const })),
      ]
    : [];

  return (
    <div>
      <Space style={{ marginBottom: 16 }} wrap>
        <span>From version:</span>
        <InputNumber
          min={1} max={currentVersion}
          value={fromVersion}
          onChange={(v) => { setFromVersion(v ?? 1); setEnabled(false); }}
          style={{ width: 80 }}
        />
        <span>To version:</span>
        <InputNumber
          min={1} max={currentVersion}
          value={toVersion}
          onChange={(v) => { setToVersion(v ?? currentVersion); setEnabled(false); }}
          style={{ width: 80 }}
        />
        <Button
          type="primary"
          size="small"
          onClick={() => setEnabled(true)}
          disabled={fromVersion >= toVersion}
        >
          Compare
        </Button>
      </Space>

      {isLoading && <Spin size="small" />}
      {isError && <Alert type="error" message="Failed to load diff" />}
      {data && (
        <Table
          size="small"
          rowKey={(r) => `${r.fieldKey}-${r.change}`}
          columns={cols}
          dataSource={rows}
          pagination={false}
          locale={{ emptyText: 'No field changes between these versions' }}
        />
      )}
    </div>
  );
}
