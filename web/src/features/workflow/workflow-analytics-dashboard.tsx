// WorkflowAnalyticsDashboard — main container orchestrating all analytics charts + filters
// Permission gate: WORKFLOW.ANALYTICS.VIEW enforced at route level (admin-workflow-analytics-page.tsx)
import { useState } from 'react';
import { Row, Col, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { WorkflowAnalyticsFilters } from './analytics/workflow-analytics-filters';
import { StateDistributionChart } from './analytics/workflow-state-distribution-chart';
import { ProcessingTimeChart } from './analytics/workflow-processing-time-chart';
import { ThroughputChart } from './analytics/workflow-throughput-chart';
import { SlaComplianceChart } from './analytics/workflow-sla-compliance-chart';
import { AgingTable } from './analytics/workflow-aging-table';
import { BottleneckChart } from './analytics/workflow-bottleneck-chart';
import {
  useStateDistribution,
  useProcessingTime,
  useThroughput,
  useSlaCompliance,
  useBottlenecks,
} from './workflow-analytics-api';
import type { AnalyticsFilters } from './analytics/workflow-analytics-filters';

const { Title } = Typography;

// Default: last 30 days
function defaultFilters(): AnalyticsFilters {
  const to = new Date();
  const from = new Date(to);
  from.setDate(from.getDate() - 30);
  return { groupBy: 'Day', from: from.toISOString(), to: to.toISOString() };
}

export function WorkflowAnalyticsDashboard() {
  const { t } = useTranslation();
  const [filters, setFilters] = useState<AnalyticsFilters>(defaultFilters);

  const { definitionId, from, to, groupBy } = filters;

  const stateDist = useStateDistribution(definitionId);
  const procTime = useProcessingTime(definitionId, from, to);
  const throughput = useThroughput(definitionId, from, to, groupBy);
  const sla = useSlaCompliance(definitionId, from, to, groupBy);
  const bottlenecks = useBottlenecks(definitionId, from, to);

  return (
    <div style={{ padding: '0 4px' }}>
      <Title level={4} style={{ marginBottom: 16 }}>
        {t('workflow.analytics.pageTitle')}
      </Title>

      <WorkflowAnalyticsFilters value={filters} onChange={setFilters} />

      {/* Row 1: State distribution + Processing time */}
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={12}>
          <StateDistributionChart
            data={stateDist.data}
            loading={stateDist.isLoading}
            error={stateDist.error}
          />
        </Col>
        <Col xs={24} lg={12}>
          <ProcessingTimeChart
            data={procTime.data}
            loading={procTime.isLoading}
            error={procTime.error}
          />
        </Col>
      </Row>

      {/* Row 2: Throughput + SLA compliance */}
      <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
        <Col xs={24} lg={12}>
          <ThroughputChart
            data={throughput.data}
            loading={throughput.isLoading}
            error={throughput.error}
          />
        </Col>
        <Col xs={24} lg={12}>
          <SlaComplianceChart
            data={sla.data}
            loading={sla.isLoading}
            error={sla.error}
          />
        </Col>
      </Row>

      {/* Row 3: Bottleneck chart */}
      <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
        <Col xs={24} lg={12}>
          <BottleneckChart
            data={bottlenecks.data}
            loading={bottlenecks.isLoading}
            error={bottlenecks.error}
          />
        </Col>
        <Col xs={24} lg={12}>
          {/* Aging table manages its own minHours state internally */}
          <AgingTable />
        </Col>
      </Row>
    </div>
  );
}
