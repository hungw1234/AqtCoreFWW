// Area/stacked line chart — SLA compliance % trend over time periods
import ReactECharts from 'echarts-for-react';
import { Card, Spin, Alert } from 'antd';
import { memo, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { GOV_COLORS } from '@/app/theme';
import type { SlaComplianceTrendDto } from '../workflow-types';

interface Props {
  data?: SlaComplianceTrendDto[];
  loading?: boolean;
  error?: Error | null;
}

export const SlaComplianceChart = memo(function SlaComplianceChart({
  data,
  loading,
  error,
}: Props) {
  const { t } = useTranslation();

  const option = useMemo(() => {
    const items = [...(data ?? [])].sort((a, b) => a.period.localeCompare(b.period));
    const periods = items.map((d) => d.period);
    const compliance = items.map((d) => Number(d.compliancePercent.toFixed(1)));
    const seriesLabel = t('workflow.analytics.compliancePercent');

    return {
      tooltip: {
        trigger: 'axis',
        formatter: (params: unknown[]) => {
          const p = params as Array<{ name: string; value: number }>;
          return `${p[0]?.name}<br/>${seriesLabel}: <b>${p[0]?.value}%</b>`;
        },
      },
      legend: { data: [seriesLabel], bottom: 0 },
      grid: { left: 8, right: 16, top: 16, bottom: 36, containLabel: true },
      xAxis: {
        type: 'category',
        data: periods.length > 0 ? periods : ['—'],
        axisLabel: { rotate: 30 },
        boundaryGap: false,
      },
      yAxis: {
        type: 'value',
        name: '%',
        min: 0,
        max: 100,
        axisLabel: { formatter: '{value}%' },
      },
      color: [GOV_COLORS.success],
      series: [
        {
          name: seriesLabel,
          type: 'line',
          smooth: true,
          data: compliance.length > 0 ? compliance : [0],
          areaStyle: { opacity: 0.15 },
          symbol: 'circle',
          symbolSize: 6,
          markLine: {
            silent: true,
            lineStyle: { color: GOV_COLORS.error, type: 'dashed' },
            data: [{ yAxis: 80, name: '80%' }],
          },
        },
      ],
    };
  }, [data, t]);

  return (
    <Card
      title={t('workflow.analytics.slaCompliance')}
      variant="borderless"
      className="gov-card-hover"
      style={{ boxShadow: 'var(--elevation-1)' }}
    >
      {error ? (
        <Alert type="error" message={t('workflow.analytics.loadError')} showIcon />
      ) : loading ? (
        <Spin style={{ display: 'block', textAlign: 'center', padding: 48 }} />
      ) : (
        <div role="img" aria-label={t('workflow.analytics.slaCompliance')}>
          <ReactECharts option={option} style={{ height: 280 }} opts={{ renderer: 'svg' }} />
        </div>
      )}
    </Card>
  );
});
