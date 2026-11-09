import { useState, useCallback } from 'react';
import { Button, Space } from 'antd';
import { DownloadOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { SodViolationsFilterPanel } from './sod-violations-filter-panel';
import { SodViolationsTable } from './sod-violations-table';
import { useSodViolations, exportSodViolations } from './sod-violations-api';
import type { SodViolationsFilters } from './sod-violations-types';
import { usePermissions } from '@/features/auth';
import { appMessage } from '@/core/api/notification-bridge';

const DEFAULT_PAGE_SIZE = 50;
const EXPORT_PERMISSION = 'IDENTITY.SOD.EXPORT_AUDIT_LOG';

/** SoD Violations tab — filter panel + Export button + paginated table */
export function SodViolationsTab() {
  const { t } = useTranslation();
  const { hasPermission } = usePermissions();
  const [filters, setFilters] = useState<SodViolationsFilters>({});
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [exporting, setExporting] = useState(false);

  const { data, isLoading } = useSodViolations(filters, page, pageSize);

  const handleFiltersChange = (newFilters: SodViolationsFilters) => {
    setFilters(newFilters);
    setPage(1); // Reset to first page on filter change
  };

  const handlePageChange = (newPage: number, newPageSize: number) => {
    setPage(newPage);
    setPageSize(newPageSize);
  };

  const handleExport = useCallback(async () => {
    setExporting(true);
    try {
      const blob = await exportSodViolations(filters);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `sod-violations-${new Date().toISOString().slice(0, 16).replace(/[:-]/g, '')}.csv`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      appMessage.success(t('sodRules.violations.exportSuccess'));
    } catch {
      appMessage.error(t('sodRules.violations.exportError'));
    } finally {
      setExporting(false);
    }
  }, [filters, t]);

  const canExport = hasPermission(EXPORT_PERMISSION);

  return (
    <div>
      <Space style={{ marginBottom: 16, justifyContent: 'space-between', width: '100%' }}>
        <SodViolationsFilterPanel
          filters={filters}
          onFiltersChange={handleFiltersChange}
          loading={isLoading}
        />
        {canExport && (
          <Button
            icon={<DownloadOutlined />}
            loading={exporting}
            onClick={handleExport}
          >
            {t('sodRules.violations.export')}
          </Button>
        )}
      </Space>
      <SodViolationsTable
        data={data?.items ?? []}
        loading={isLoading}
        page={page}
        pageSize={pageSize}
        total={data?.totalCount ?? 0}
        onPageChange={handlePageChange}
      />
    </div>
  );
}
