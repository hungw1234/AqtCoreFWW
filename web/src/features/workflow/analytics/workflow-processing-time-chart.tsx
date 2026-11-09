// Bar chart — average processing time (minutes) per state
import ReactECharts from 'echarts-for-react';
import { Card, Spin, Alert } from 'antd';
import { memo, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { GOV_COLORS } from '@/app/theme';
import type { StateProcessingTimeDto } from '../workflow-types';

interface Props {
  data?: StateProcessingTimeDto[];
  loading?: boolean;
  error?: Error | null;
}

export const ProcessingTimeChart = memo(function ProcessingTimeChart({
  data,
  loading,
  error,
}: Props) {
  const { t } = useTranslation();

  const option = useMemo(() => {
    const items = data ?? [];
    const categories = items.map((d) => d.stateName);
    const avgValues = items.map((d) => Math.round(d.avgMinutes));
    const medianValues = items.map((d) => Math.round(d.medianMinutes));
    const avgLabel = t('workflow.analytics.avgMinutes');
    const medianLabel = t('workflow.analytics.medianMinutes');
    return {
      tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
      legend: { data: [avgLabel, medianLabel], bottom: 0 },
      grid: { left: 8, right: 16, top: 16, bottom: 48, containLabel: true },
      xAxis: {
        type: 'category',
        data: categories.length > 0 ? categories : ['—'],
        axisLabel: { rotate: 30, overflow: 'truncate', width: 80 },
      },
      yAxis: {
        type: 'value',
        name: t('workflow.analytics.minutesAxis'),
        minInterval: 1,
      },
      color: [GOV_COLORS.navy, GOV_COLORS.gold],
      series: [
        {
          name: avgLabel,
          type: 'bar',
          barMaxWidth: 32,
          itemStyle: { borderRadius: [4, 4, 0, 0] },
          data: avgValues.length > 0 ? avgValues : [0],
        },
        {
          name: medianLabel,
          type: 'bar',
          barMaxWidth: 32,
          itemStyle: { borderRadius: [4, 4, 0, 0] },
          data: medianValues.length > 0 ? medianValues : [0],
        },
      ],
    };
  }, [data, t]);

  return (
    <Card
      title={t('workflow.analytics.processingTime')}
      variant="borderless"
      className="gov-card-hover"
      style={{ boxShadow: 'var(--elevation-1)' }}
    >
      {error ? (
        <Alert type="error" message={t('workflow.analytics.loadError')} showIcon />
      ) : loading ? (
        <Spin style={{ display: 'block', textAlign: 'center', padding: 48 }} />
      ) : (
        <div role="img" aria-label={t('workflow.analytics.processingTime')}>
          <ReactECharts option={option} style={{ height: 280 }} opts={{ renderer: 'svg' }} />
        </div>
      )}
    </Card>
  );
});
