// Horizontal bar chart — avg dwell time per state sorted desc (bottleneck detection)
import ReactECharts from 'echarts-for-react';
import { Card, Spin, Alert } from 'antd';
import { memo, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { GOV_COLORS } from '@/app/theme';
import type { BottleneckDto } from '../workflow-types';

interface Props {
  data?: BottleneckDto[];
  loading?: boolean;
  error?: Error | null;
}

export const BottleneckChart = memo(function BottleneckChart({ data, loading, error }: Props) {
  const { t } = useTranslation();

  const option = useMemo(() => {
    // Sort descending by avgDwellMinutes so worst bottleneck is at top
    const items = [...(data ?? [])].sort((a, b) => b.avgDwellMinutes - a.avgDwellMinutes);
    const categories = items.map((d) => d.stateName);
    const values = items.map((d) => Math.round(d.avgDwellMinutes));

    return {
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: unknown[]) => {
          const p = params as Array<{ name: string; value: number }>;
          return `${p[0]?.name}<br/>${t('workflow.analytics.avgDwellMinutes')}: <b>${p[0]?.value}</b>`;
        },
      },
      grid: { left: 16, right: 48, top: 8, bottom: 8, containLabel: true },
      xAxis: {
        type: 'value',
        name: t('workflow.analytics.avgDwellMinutesAxis'),
        nameLocation: 'end',
        minInterval: 1,
      },
      yAxis: {
        type: 'category',
        data: categories.length > 0 ? categories : [t('workflow.analytics.noData')],
        axisLabel: { width: 120, overflow: 'truncate' },
      },
      color: [GOV_COLORS.error],
      series: [
        {
          type: 'bar',
          data: values.length > 0 ? values : [0],
          barMaxWidth: 32,
          itemStyle: { borderRadius: [0, 4, 4, 0] },
          label: { show: true, position: 'right', color: GOV_COLORS.textPrimary },
        },
      ],
    };
  }, [data, t]);

  return (
    <Card
      title={t('workflow.analytics.bottleneck')}
      variant="borderless"
      className="gov-card-hover"
      style={{ boxShadow: 'var(--elevation-1)' }}
    >
      {error ? (
        <Alert type="error" message={t('workflow.analytics.loadError')} showIcon />
      ) : loading ? (
        <Spin style={{ display: 'block', textAlign: 'center', padding: 48 }} />
      ) : (
        <div role="img" aria-label={t('workflow.analytics.bottleneck')}>
          <ReactECharts option={option} style={{ height: 280 }} opts={{ renderer: 'svg' }} />
        </div>
      )}
    </Card>
  );
});
