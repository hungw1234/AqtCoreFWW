// Audit export dropdown — downloads JSON/CSV via /workflow/instances/{id}/audit-export

import { useState } from 'react';
import { Dropdown, Button } from 'antd';
import { DownloadOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/core/api';
import { appMessage } from '@/core/api/notification-bridge';

/** AuditExportFormat mirrors BE enum: Json=0, Csv=1, Pdf=2 */
type ExportFormat = 'json' | 'csv';

interface Props {
  instanceId: string;
}

/**
 * Export button with dropdown for JSON/CSV download.
 * PDF deferred — button hidden until composer ships in follow-up.
 */
export function WorkflowAuditExportButton({ instanceId }: Props) {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(false);

  const handleDownload = async (format: ExportFormat) => {
    setLoading(true);
    try {
      // Response is binary — disable envelope unwrapping by using responseType 'blob'.
      // Backend returns FileContentResult (not wrapped in ApiResponse envelope).
      const res = await apiClient.get(
        `/workflow/instances/${instanceId}/audit-export`,
        { params: { format }, responseType: 'blob' },
      );

      const blob = new Blob([res.data], {
        type: format === 'json' ? 'application/json' : 'text/csv',
      });
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = extractFilename(res.headers['content-disposition']) ??
        `workflow-audit-${instanceId}.${format}`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);

      appMessage.success(t('workflow.audit.exportSuccess'));
    } catch {
      appMessage.error(t('common.error'));
    } finally {
      setLoading(false);
    }
  };

  const items = [
    {
      key: 'json',
      label: t('workflow.audit.formatJson'),
      onClick: () => handleDownload('json'),
    },
    {
      key: 'csv',
      label: t('workflow.audit.formatCsv'),
      onClick: () => handleDownload('csv'),
    },
  ];

  return (
    <Dropdown menu={{ items }} trigger={['click']}>
      <Button icon={<DownloadOutlined />} loading={loading}>
        {t('workflow.audit.exportButton')}
      </Button>
    </Dropdown>
  );
}

/** Parse filename from Content-Disposition header — falls back to null if absent/malformed */
function extractFilename(contentDisposition?: string): string | null {
  if (!contentDisposition) return null;
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/.exec(contentDisposition);
  return match?.[1] ?? null;
}
