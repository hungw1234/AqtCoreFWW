// Line chart — Top-5 users by transition count + aggregated "Others" line
// Each user is a separate series; "Others" aggregates users ranked 6+ by total transitions.
// BE sends IsOthers=true / UserId=Guid.Empty for the "Others" bucket.
import ReactECharts from 'echarts-for-react';
import { Card, Spin, Alert } from 'antd';
import { memo, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { UserThroughputDto } from '../workflow-types';

// Sentinel value emitted by BE for the "Others" aggregate bucket
const OTHERS_USER_ID = '00000000-0000-0000-0000-000000000000';

// Six distinct colors: 5 user lines + 1 "Others" line (grey last)
const SERIES_COLORS = ['#007BFF', '#28A745', '#FFC107', '#DC3545', '#6F42C1', '#ADB5BD'];

interface Props {
  data?: UserThroughputDto[];
  loading?: boolean;
  error?: Error | null;
}

/** Truncate a GUID to first 8 chars for compact chart legend display */
function shortId(userId: string): string {
  return userId.slice(0, 8);
}

export const ThroughputChart = memo(function ThroughputChart({ data, loading, error }: Props) {
  const { t } = useTranslation();

  const option = useMemo(() => {
    const items = data ?? [];

    // Collect sorted periods
    const periodSet = new Set<string>();
    for (const d of items) periodSet.add(d.period);
    const periods = Array.from(periodSet).sort();

    // Group rows by userId — "Others" rows keyed by sentinel
    const seriesMap = new Map<string, Map<string, number>>();
    for (const d of items) {
      const key = d.isOthers ? OTHERS_USER_ID : d.userId;
      if (!seriesMap.has(key)) seriesMap.set(key, new Map());
      seriesMap.get(key)!.set(d.period, d.transitionCount);
    }

    // Order: non-Others first (insertion order reflects BE top-5 ordering), Others last
    const orderedKeys = [
      ...Array.from(seriesMap.keys()).filter((k) => k !== OTHERS_USER_ID),
      ...(seriesMap.has(OTHERS_USER_ID) ? [OTHERS_USER_ID] : []),
    ];

    const othersLabel = t('workflow.analytics.throughputOthers');

    const series = orderedKeys.map((userId, idx) => {
      const isOthers = userId === OTHERS_USER_ID;
      const name = isOthers ? othersLabel : shortId(userId);
      const periodData = seriesMap.get(userId)!;
      return {
        name,
        type: 'line' as const,
        smooth: true,
        data: periods.map((p) => periodData.get(p) ?? 0),
        areaStyle: isOthers ? undefined : { opacity: 0.04 },
        lineStyle: isOthers ? { type: 'dashed' as const, width: 1 } : { width: 2 },
        symbol: 'circle',
        symbolSize: 5,
        color: SERIES_COLORS[idx % SERIES_COLORS.length],
      };
    });

    const legendData = orderedKeys.map((userId, idx) => ({
      name: userId === OTHERS_USER_ID ? othersLabel : shortId(userId),
      itemStyle: { color: SERIES_COLORS[idx % SERIES_COLORS.length] },
    }));

    return {
      tooltip: { trigger: 'axis' },
      legend: {
        data: legendData,
        bottom: 0,
        type: 'scroll' as const,
      },
      grid: { left: 8, right: 16, top: 16, bottom: 48, containLabel: true },
      xAxis: {
        type: 'category' as const,
        data: periods.length > 0 ? periods : ['—'],
        axisLabel: { rotate: 30 },
        boundaryGap: false,
      },
      yAxis: {
        type: 'value' as const,
        name: t('workflow.analytics.transitionsAxis'),
        minInterval: 1,
      },
      series: series.length > 0 ? series : [{
        name: t('workflow.analytics.transitions'),
        type: 'line' as const,
        data: [0],
      }],
    };
  }, [data, t]);

  return (
    <Card
      title={t('workflow.analytics.throughput')}
      variant="borderless"
      className="gov-card-hover"
      style={{ boxShadow: 'var(--elevation-1)' }}
    >
      {error ? (
        <Alert type="error" message={t('workflow.analytics.loadError')} showIcon />
      ) : loading ? (
        <Spin style={{ display: 'block', textAlign: 'center', padding: 48 }} />
      ) : (
        <div role="img" aria-label={t('workflow.analytics.throughput')}>
          <ReactECharts option={option} style={{ height: 280 }} opts={{ renderer: 'svg' }} />
        </div>
      )}
    </Card>
  );
});
