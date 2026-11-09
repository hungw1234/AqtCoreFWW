// Form analytics panel — simple stats cards + submissions-by-date sparkline
import { Statistic, Row, Col, Table, Spin, Alert } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useFormAnalytics } from './form-api';

interface Props {
  templateId: string;
}

export function FormAnalyticsPanel({ templateId }: Props) {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useFormAnalytics(templateId);

  if (isLoading) return <Spin size="small" />;
  if (isError || !data) return <Alert type="warning" message={t('page.forms.detail.analytics.unavailable')} />;

  const cols: ColumnsType<{ date: string; count: number }> = [
    { title: t('page.forms.detail.analytics.dateCol'), dataIndex: 'date', key: 'date', width: 120 },
    { title: t('page.forms.detail.analytics.submissionsCol'), dataIndex: 'count', key: 'count', width: 100 },
  ];

  return (
    <div>
      <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
        <Col xs={12} sm={6}>
          <Statistic title={t('page.forms.detail.analytics.total')} value={data.totalSubmissions} />
        </Col>
        <Col xs={12} sm={6}>
          <Statistic title={t('page.forms.detail.analytics.pending')} value={data.pendingCount} valueStyle={{ color: '#d4a017' }} />
        </Col>
        <Col xs={12} sm={6}>
          <Statistic title={t('page.forms.detail.analytics.approved')} value={data.approvedCount} valueStyle={{ color: '#3f8600' }} />
        </Col>
        <Col xs={12} sm={6}>
          <Statistic title={t('page.forms.detail.analytics.rejected')} value={data.rejectedCount} valueStyle={{ color: '#cf1322' }} />
        </Col>
      </Row>
      <Table
        size="small"
        rowKey="date"
        columns={cols}
        dataSource={data.submissionsByDate}
        pagination={false}
        scroll={{ y: 200 }}
        locale={{ emptyText: t('page.forms.detail.analytics.noData') }}
      />
    </div>
  );
}
