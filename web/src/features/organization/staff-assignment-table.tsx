import { Table, Tag, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useOrgUnitStaff } from './org-api';
import type { StaffAssignmentDto } from './org-types';

const { Text } = Typography;

interface StaffAssignmentTableProps {
  unitId: string;
}

// StaffAssignmentTable — shows staff members assigned to a given org unit
export function StaffAssignmentTable({ unitId }: StaffAssignmentTableProps) {
  const { t } = useTranslation();
  const { data: staff = [], isFetching } = useOrgUnitStaff(unitId);

  const COLUMNS: ColumnsType<StaffAssignmentDto> = [
    {
      title: t('org.col.fullName'),
      dataIndex: 'fullName',
      key: 'fullName',
      ellipsis: true,
      render: (v?: string) => v || '—',
    },
    {
      title: 'Email',
      dataIndex: 'email',
      key: 'email',
      ellipsis: true,
      render: (v?: string) => v ? <Text style={{ fontSize: 12 }}>{v}</Text> : '—',
    },
    {
      title: t('org.col.roleInUnit'),
      dataIndex: 'roleInOrg',
      key: 'roleInOrg',
      width: 240,
      ellipsis: { showTitle: true },
      render: (v?: string) => v
        ? <Tag style={{ maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={v}>{v}</Tag>
        : '—',
    },
  ];

  return (
    <Table<StaffAssignmentDto>
      rowKey="id"
      columns={COLUMNS}
      dataSource={staff}
      loading={isFetching}
      size="small"
      scroll={{ x: 600 }}
      pagination={{ pageSize: 10, showSizeChanger: false }}
      locale={{ emptyText: t('org.emptyStaff') }}
    />
  );
}
