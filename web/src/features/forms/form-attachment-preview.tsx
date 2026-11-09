// Attachment preview for File-type fields in submission detail.
// Reads file metadata + scan status via useFileScanStatus (GET /api/v1/files/{fileId}/metadata,
// polling until the scan reaches a terminal status). MIME type + name drive the preview; the scan
// status gates it — a Quarantined or Rejected file never renders a download link because the byte
// route (GET /files/{fileId}) 404s for any non-Available status.
// Available images: <img> thumbnail (max 200px), bytes fetched as an authenticated blob → object URL
// Available PDFs/others: icon + filename link (downloads on click via apiClient)
import { useEffect, useState } from 'react';
import { Spin, Tag, Tooltip, Typography } from 'antd';
import { FilePdfOutlined, FileOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { downloadFile, useFileBlob, useFileScanStatus } from '@/features/files/file-api';

const { Link, Text } = Typography;

interface Props {
  fileId: string;
}

export function AttachmentPreview({ fileId }: Props) {
  const { t } = useTranslation();
  const { data: file, isLoading } = useFileScanStatus(fileId);
  const isAvailable = file?.status === 'Available';
  const isImage = Boolean(file?.contentType?.startsWith('image/'));

  // Only images need their bytes up front (to render a thumbnail); PDFs/others download on click.
  // Gate the fetch on Available — the byte route 404s for Quarantined/Rejected, so fetching earlier
  // would only error.
  const { data: blob } = useFileBlob(fileId, isImage && isAvailable);
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!blob) {
      setObjectUrl(null);
      return;
    }
    const url = URL.createObjectURL(blob);
    setObjectUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [blob]);

  if (isLoading) return <Spin size="small" />;
  if (!file) return <Text type="secondary">{t('forms.attachment.notFound', { defaultValue: 'Không tìm thấy tệp' })}</Text>;

  const contentType = file.contentType;
  const fileName = file.originalFileName;
  const linkStyle = { display: 'inline-flex', alignItems: 'center', gap: 6, cursor: 'pointer' };
  const rowStyle = { display: 'inline-flex', alignItems: 'center', gap: 6 };

  // Scan still running — the byte route 404s while Quarantined, so show progress, not a dead link.
  if (file.status === 'Quarantined') {
    return (
      <Tooltip title={fileName}>
        <span style={rowStyle}>
          <Spin size="small" />
          <span>{fileName}</span>
          <Tag color="processing">{t('forms.attachment.scanning', { defaultValue: 'Đang quét virus…' })}</Tag>
        </span>
      </Tooltip>
    );
  }

  // Virus found — the file was deleted server-side; never offer a download.
  if (file.status === 'Rejected') {
    return (
      <Tooltip title={fileName}>
        <span style={rowStyle}>
          <FileOutlined style={{ fontSize: 20, color: '#ff4d4f' }} />
          <span style={{ color: '#ff4d4f' }}>{fileName}</span>
          <Tag color="error">{t('forms.attachment.rejected', { defaultValue: 'Tệp bị từ chối (nhiễm virus)' })}</Tag>
        </span>
      </Tooltip>
    );
  }

  // Image preview — thumbnail from the authenticated blob; click saves the file.
  if (contentType.startsWith('image/')) {
    return (
      <Tooltip title={fileName}>
        <Link onClick={() => downloadFile(fileId, fileName)} style={{ cursor: 'pointer' }}>
          {objectUrl ? (
            <img
              src={objectUrl}
              alt={fileName}
              style={{ maxWidth: 200, maxHeight: 200, objectFit: 'contain', border: '1px solid #eee', borderRadius: 4 }}
            />
          ) : (
            <Spin size="small" />
          )}
        </Link>
      </Tooltip>
    );
  }

  // PDF preview
  if (contentType === 'application/pdf') {
    return (
      <Tooltip title={fileName}>
        <Link onClick={() => downloadFile(fileId, fileName)} style={linkStyle}>
          <FilePdfOutlined style={{ fontSize: 24, color: '#ff4d4f' }} />
          <span>{fileName}</span>
        </Link>
      </Tooltip>
    );
  }

  // Generic file
  return (
    <Tooltip title={fileName}>
      <Link onClick={() => downloadFile(fileId, fileName)} style={linkStyle}>
        <FileOutlined style={{ fontSize: 20, color: '#1677ff' }} />
        <span>{fileName}</span>
      </Link>
    </Tooltip>
  );
}

/** Render a field value that might be a file ID or array of file IDs */
export function FieldValueWithAttachment({ fieldKey, value }: { fieldKey: string; value: unknown }) {
  void fieldKey; // reserved for future label lookup

  if (!value) return <Text type="secondary">—</Text>;

  const strVal = String(value);

  // Detect GUID-shaped file IDs (36 chars with hyphens)
  const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (guidRegex.test(strVal)) {
    return <AttachmentPreview fileId={strVal} />;
  }

  // Plain value
  return <span>{strVal}</span>;
}
