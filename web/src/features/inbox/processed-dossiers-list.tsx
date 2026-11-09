// processed-dossiers-list.tsx — View 2: cursor-paginated list of BP-Run dossiers the
// current user has already processed (transitioned at least once).
// BP display name resolved client-side from entityId → BpActiveDto.id (O(1) Map, no N+1).
// Row click → navigate to /business-process-runs/$id (workflowInstancePublicId).

import { useMemo } from 'react';
import { Table, Tag, Button, Space } from 'antd';
import { ArrowRightOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useMyProcessedInfinite } from './my-processed-api';
import type { MyProcessedInstanceDto } from './my-processed-api';
import { useActiveBps } from '@/features/business-process/business-process-run-api';

// ─── Props ────────────────────────────────────────────────────────────────────

// No external props — self-contained data fetching

// ─── Component ───────────────────────────────────────────────────────────────

export function ProcessedDossiersList() {
  const { t } = useTranslation();
  const navigate = useNavigate();

  // Infinite cursor-based query — accumulate all loaded pages
  const {
    data,
    isFetching,
    isFetchingNextPage,
    hasNextPage,
    fetchNextPage,
  } = useMyProcessedInfinite();

  // Active BP list for name resolution — fetched once, shared with other consumers
  const { data: activeBpsData } = useActiveBps();

  // Build id → name map once (O(n) build, O(1) lookup per row — no N+1)
  const bpNameMap = useMemo(() => {
    const items = activeBpsData?.items ?? [];
    return new Map(items.map((bp) => [bp.id.toLowerCase(), bp.name]));
  }, [activeBpsData]);

  // Flatten all accumulated pages into a single array
  const rows: MyProcessedInstanceDto[] = useMemo(
    () => data?.pages.flatMap((p) => p.items) ?? [],
    [data],
  );

  const isLoading = isFetching && rows.length === 0;

  const COLUMNS: ColumnsType<MyProcessedInstanceDto> = [
    {
      title: t('page.myDossiers.processed.col.bpName'),
      key: 'bpName',
      ellipsis: true,
      render: (_, record) => {
        const name = bpNameMap.get(record.entityId.toLowerCase());
        return name ?? <span style={{ color: '#999' }}>—</span>;
      },
    },
    {
      title: t('page.myDossiers.processed.col.currentState'),
      dataIndex: 'currentStateName',
      key: 'currentStateName',
      width: 160,
      render: (v: string) => <Tag color="blue">{v || '—'}</Tag>,
    },
    {
      title: t('page.myDossiers.processed.col.startedAt'),
      dataIndex: 'startedAt',
      key: 'startedAt',
      width: 150,
      render: (v?: string | null) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      title: t('page.myDossiers.processed.col.isFinal'),
      dataIndex: 'isFinal',
      key: 'isFinal',
      width: 130,
      render: (v: boolean) =>
        v ? (
          <Tag color="green">{t('page.myDossiers.processed.statusFinal')}</Tag>
        ) : (
          <Tag color="orange">{t('page.myDossiers.processed.statusRunning')}</Tag>
        ),
    },
    {
      title: t('page.inbox.col.actions'),
      key: 'actions',
      width: 80,
      render: (_, record) => (
        <Space>
          <Button
            size="small"
            type="primary"
            icon={<ArrowRightOutlined />}
            onClick={() =>
              navigate({
                to: '/business-process-runs/$id',
                params: { id: record.workflowInstancePublicId },
              })
            }
          />
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Table<MyProcessedInstanceDto>
        rowKey="workflowInstancePublicId"
        columns={COLUMNS}
        dataSource={rows}
        loading={isLoading}
        size="small"
        scroll={{ x: 800 }}
        locale={{ emptyText: t('page.myDossiers.processed.empty') }}
        pagination={false}
      />
      {hasNextPage && (
        <div style={{ textAlign: 'center', marginTop: 16 }}>
          <Button
            onClick={() => fetchNextPage()}
            loading={isFetchingNextPage}
            disabled={!hasNextPage || isFetchingNextPage}
          >
            {t('page.myDossiers.processed.loadMore')}
          </Button>
        </div>
      )}
    </div>
  );
}
