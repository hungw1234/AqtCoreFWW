// Donut chart — count of workflow instances per state for a given definition
import ReactECharts from 'echarts-for-react';
import { Card, Spin, Alert } from 'antd';
import { memo, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { GOV_COLORS } from '@/app/theme';
import type { StateDistributionDto } from '../workflow-types';

interface Props {
  data?: StateDistributionDto[];
  loading?: boolean;
  error?: Error | null;
}

export const StateDistributionChart = memo(function StateDistributionChart({
  data,
  loading,
  error,
}: Props) {
  const { t } = useTranslation();

  const option = useMemo(() => {
    const series = (data ?? []).map((d) => ({ name: d.stateName, value: d.count }));
    return {
      tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
      legend: { bottom: 0, type: 'scroll' },
      color: [
        GOV_COLORS.navy,
        GOV_COLORS.gold,
        GOV_COLORS.success,
        GOV_COLORS.error,
        GOV_COLORS.navyLight,
        GOV_COLORS.actionBlue,
      ],
      series: [
        {
          type: 'pie',
          radius: ['42%', '68%'],
          avoidLabelOverlap: false,
          itemStyle: { borderRadius: 4, borderColor: '#fff', borderWidth: 2 },
          label: { show: false },
          emphasis: { label: { show: true, fontSize: 14, fontWeight: 700 } },
          data: series.length > 0 ? series : [{ name: t('workflow.analytics.noData'), value: 1 }],
        },
      ],
    };
  }, [data, t]);

  return (
    <Card
      title={t('workflow.analytics.stateDistribution')}
      variant="borderless"
      className="gov-card-hover"
      style={{ boxShadow: 'var(--elevation-1)' }}
    >
      {error ? (
        <Alert type="error" message={t('workflow.analytics.loadError')} showIcon />
      ) : loading ? (
        <Spin style={{ display: 'block', textAlign: 'center', padding: 48 }} />
      ) : (
        <div role="img" aria-label={t('workflow.analytics.stateDistribution')}>
          <ReactECharts option={option} style={{ height: 280 }} opts={{ renderer: 'svg' }} />
        </div>
      )}
    </Card>
  );
});
