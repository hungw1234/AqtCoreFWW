import { useState } from 'react';
import { Table, Tag, Button, Alert, Tooltip } from 'antd';
import { DownloadOutlined, PlayCircleOutlined, LoadingOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useReportDefinitions, useReportExecution, downloadReport } from './report-api';
import { ReportRunModal } from './report-run-modal';
import type { ReportExecutionDto, ExecutionStatus } from './report-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';

// ─── Single-execution polling row — one hook per execution, stable mount ─────

interface PollingCellProps {
  executionId: string;
  onData: (dto: ReportExecutionDto) => void;
}

/** Mounts permanently once an executionId is tracked; propagates polled data upward */
function ExecutionPoller({ executionId, onData }: PollingCellProps) {
  const { data } = useReportExecution(executionId);
  if (data) onData(data);
  return null;
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export function ReportExecutionsPage() {
  const { t } = useTranslation();
  const { data: definitions = [] } = useReportDefinitions();
  const [runOpen, setRunOpen] = useState(false);

  // Stable list of tracked execution IDs — only grows, never shrinks (preserves hook count)
  const [trackedIds, setTrackedIds] = useState<string[]>([]);

  // Latest data for each tracked execution, keyed by id
  const [executionMap, setExecutionMap] = useState<Record<string, ReportExecutionDto>>({});

  // Status display config — built inside component to use t()
  const STATUS_CONFIG: Record<ExecutionStatus, { color: string; label: string }> = {
    Queued: { color: 'default', label: t('page.reports.executions.statusQueued') },
    Running: { color: 'processing', label: t('page.reports.executions.statusRunning') },
    Done: { color: 'success', label: t('page.reports.executions.statusDone') },
    Failed: { color: 'error', label: t('page.reports.executions.statusFailed') },
  };

  function handleData(dto: ReportExecutionDto) {
    setExecutionMap((prev) =>
      prev[dto.id]?.status === dto.status ? prev : { ...prev, [dto.id]: dto },
    );
  }

  function handleExecutionStarted(id: string) {
    setTrackedIds((prev) => (prev.includes(id) ? prev : [id, ...prev]));
    setRunOpen(false);
  }

  function getDefinitionName(defId: string) {
    const def = definitions.find((d) => d.id === defId);
    return def?.nameVi || def?.name || defId;
  }

  // Build display rows from latest polled data, newest first
  const rows = trackedIds
    .map((id) => executionMap[id])
    .filter((d): d is ReportExecutionDto => Boolean(d))
    .sort((a, b) => dayjs(b.requestedAt).valueOf() - dayjs(a.requestedAt).valueOf());

  const COLUMNS: ColumnsType<ReportExecutionDto> = [
    {
      title: t('page.reports.executions.colDefinition'),
      key: 'definition',
      ellipsis: true,
      render: (_, r) => getDefinitionName(r.reportDefinitionId),
    },
    {
      title: t('page.reports.executions.colRequestedAt'),
      dataIndex: 'requestedAt',
      key: 'requestedAt',
      width: 140,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
    {
      title: t('page.reports.executions.colStatus'),
      dataIndex: 'status',
      key: 'status',
      width: 130,
      render: (v: ExecutionStatus) => {
        const cfg = STATUS_CONFIG[v];
        return (
          <Tag color={cfg.color} icon={v === 'Running' ? <LoadingOutlined /> : undefined}>
            {cfg.label}
          </Tag>
        );
      },
    },
    {
      title: t('page.reports.executions.colCompletedAt'),
      dataIndex: 'completedAt',
      key: 'completedAt',
      width: 140,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      title: t('page.reports.executions.colError'),
      dataIndex: 'errorMessage',
      key: 'errorMessage',
      ellipsis: true,
      render: (v?: string) =>
        v ? (
          <Tooltip title={v}>
            <span style={{ color: '#ff4d4f' }}>{v}</span>
          </Tooltip>
        ) : (
          '—'
        ),
    },
    {
      title: '',
      key: 'actions',
      width: 110,
      render: (_, r) =>
        r.status === 'Done' && r.hasResult ? (
          <Tooltip title={t('page.reports.executions.btnDownload', 'Tải xuống')}>
            <Button size="small" icon={<DownloadOutlined />} onClick={() => downloadReport(r.id)} />
          </Tooltip>
        ) : null,
    },
  ];

  return (
    <>
      <AdminPageHeader
        title={t('page.reports.executions.title')}
        actions={
          <Button type="primary" icon={<PlayCircleOutlined />} onClick={() => setRunOpen(true)}>
            {t('page.reports.executions.btnRun')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        {trackedIds.length === 0 && (
          <Alert
            type="info"
            showIcon
            message={t('page.reports.executions.emptySession')}
            style={{ margin: 16 }}
          />
        )}
        <Table<ReportExecutionDto>
          rowKey="id"
          columns={COLUMNS}
          dataSource={rows}
          size="small"
          pagination={{ pageSize: 20 }}
          locale={{ emptyText: t('common.loading') }}
        />
      </AdminContentCard>

      {/* One stable poller per tracked ID — never removed, preserves hook order */}
      {trackedIds.map((id) => (
        <ExecutionPoller key={id} executionId={id} onData={handleData} />
      ))}

      <ReportRunModal
        open={runOpen}
        definitions={definitions}
        onClose={() => setRunOpen(false)}
        onExecutionStarted={handleExecutionStarted}
      />
    </>
  );
}
