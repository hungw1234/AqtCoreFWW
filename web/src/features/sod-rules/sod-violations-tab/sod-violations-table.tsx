import { Table, Tag, Typography, Tooltip } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import type { SodViolationLogDto } from './sod-violations-types';

const { Text } = Typography;

// Severity tag colors — escalating risk level
const SEVERITY_COLORS: Record<string, string> = {
  Low: 'blue',
  Medium: 'orange',
  High: 'red',
};

interface Props {
  data: SodViolationLogDto[];
  loading?: boolean;
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (page: number, pageSize: number) => void;
}

/** Table displaying SoD violation audit log entries */
export function SodViolationsTable({
  data,
  loading,
  page,
  pageSize,
  total,
  onPageChange,
}: Props) {
  const { t } = useTranslation();

  const columns: ColumnsType<SodViolationLogDto> = [
    {
      title: t('sodRules.violations.columns.detectedAt', 'Thoi diem'),
      dataIndex: 'detectedAt',
      key: 'detectedAt',
      width: 170,
      render: (v: string) => new Date(v).toLocaleString('vi-VN'),
    },
    {
      title: t('sodRules.violations.columns.rule', 'Quy tac'),
      key: 'rule',
      width: 220,
      render: (_, record) => (
        <span>
          <Tag>{record.permissionCodeA}</Tag>
          <span style={{ margin: '0 4px' }}>&harr;</span>
          <Tag>{record.permissionCodeB}</Tag>
        </span>
      ),
    },
    {
      title: t('sodRules.violations.columns.severity', 'Muc do'),
      dataIndex: 'severity',
      key: 'severity',
      width: 100,
      render: (v: string) => (
        <Tag color={SEVERITY_COLORS[v] ?? 'default'}>
          {t(`sodSeverity.${v.toLowerCase()}`, v)}
        </Tag>
      ),
    },
    {
      title: t('sodRules.violations.columns.justification', 'Ly do'),
      dataIndex: 'justification',
      key: 'justification',
      ellipsis: true,
      render: (v?: string) =>
        v ? (
          <Tooltip title={v}>
            <Text ellipsis style={{ maxWidth: 200 }}>
              {v}
            </Text>
          </Tooltip>
        ) : (
          <Text type="secondary">-</Text>
        ),
    },
  ];

  return (
    <Table<SodViolationLogDto>
      rowKey="id"
      columns={columns}
      dataSource={data}
      loading={loading}
      size="small"
      pagination={{
        current: page,
        pageSize,
        total,
        showSizeChanger: true,
        pageSizeOptions: ['20', '50', '100'],
        showTotal: (count) => `${t('common.total', 'Tong')} ${count}`,
        onChange: onPageChange,
      }}
      locale={{
        emptyText: t('sodRules.violations.empty', 'Khong co du lieu'),
      }}
    />
  );
}
