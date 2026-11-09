// Analytics filter bar — date range + definition picker + groupBy selector
import { Card, DatePicker, Select, Space, Row, Col } from 'antd';
import { memo } from 'react';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useWorkflowDefinitions } from '../workflow-api';
import type { GroupByPeriod } from '../workflow-types';

const { RangePicker } = DatePicker;

export interface AnalyticsFilters {
  definitionId?: string;
  from?: string;
  to?: string;
  groupBy: GroupByPeriod;
}

interface Props {
  value: AnalyticsFilters;
  onChange: (filters: AnalyticsFilters) => void;
}

export const WorkflowAnalyticsFilters = memo(function WorkflowAnalyticsFilters({
  value,
  onChange,
}: Props) {
  const { t } = useTranslation();
  // Load first page of definitions for the picker (20 should cover most tenants)
  const { data: defsPage } = useWorkflowDefinitions(1, 20);
  const definitions = defsPage?.items ?? [];

  const groupByOptions: { label: string; value: GroupByPeriod }[] = [
    { label: t('workflow.analytics.filter.groupByDay'), value: 'Day' },
    { label: t('workflow.analytics.filter.groupByWeek'), value: 'Week' },
    { label: t('workflow.analytics.filter.groupByMonth'), value: 'Month' },
  ];

  return (
    <Card
      size="small"
      variant="borderless"
      style={{ boxShadow: 'var(--elevation-1)', marginBottom: 16 }}
    >
      <Row gutter={[16, 8]} align="middle">
        <Col xs={24} sm={12} md={8}>
          <Space direction="vertical" size={2} style={{ width: '100%' }}>
            <span style={{ fontSize: 12, color: '#888' }}>
              {t('workflow.analytics.filter.definition')}
            </span>
            <Select
              allowClear
              placeholder={t('workflow.selectDefinition')}
              value={value.definitionId}
              onChange={(v) => onChange({ ...value, definitionId: v })}
              style={{ width: '100%' }}
              options={definitions.map((d) => ({ label: d.name, value: d.id }))}
            />
          </Space>
        </Col>
        <Col xs={24} sm={12} md={10}>
          <Space direction="vertical" size={2} style={{ width: '100%' }}>
            <span style={{ fontSize: 12, color: '#888' }}>
              {t('workflow.analytics.filter.dateRange')}
            </span>
            <RangePicker
              style={{ width: '100%' }}
              value={
                value.from && value.to
                  ? [dayjs(value.from), dayjs(value.to)]
                  : null
              }
              onChange={(dates) =>
                onChange({
                  ...value,
                  from: dates?.[0]?.toISOString(),
                  to: dates?.[1]?.toISOString(),
                })
              }
            />
          </Space>
        </Col>
        <Col xs={24} sm={12} md={6}>
          <Space direction="vertical" size={2} style={{ width: '100%' }}>
            <span style={{ fontSize: 12, color: '#888' }}>
              {t('workflow.analytics.filter.groupBy')}
            </span>
            <Select
              value={value.groupBy}
              onChange={(v) => onChange({ ...value, groupBy: v })}
              style={{ width: '100%' }}
              options={groupByOptions}
            />
          </Space>
        </Col>
      </Row>
    </Card>
  );
});
