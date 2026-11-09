// Maps a file's MIME content type to a representative Ant Design icon.
// Used by the files list (LOẠI column) so the type reads at a glance instead of a raw MIME string;
// the original content type is preserved in a tooltip so no information is lost.
import {
  FilePdfOutlined,
  FileWordOutlined,
  FileExcelOutlined,
  FilePptOutlined,
  FileImageOutlined,
  FileZipOutlined,
  FileTextOutlined,
  FileOutlined,
} from '@ant-design/icons';
import { Tooltip } from 'antd';
import type { ReactNode } from 'react';

// Brand-ish colours per family so the icons are scannable. Generic falls back to a neutral grey.
interface IconSpec {
  icon: ReactNode;
  color: string;
}

// Ordered most-specific → generic. `startsWith` on family prefixes keeps the table small while
// covering the Office Open XML long types (…wordprocessingml, …spreadsheetml, …presentationml)
// and the legacy short types (msword, vnd.ms-excel, vnd.ms-powerpoint) in one entry each.
function resolveIconSpec(contentType: string): IconSpec {
  const ct = contentType.toLowerCase();

  if (ct === 'application/pdf') return { icon: <FilePdfOutlined />, color: '#ff4d4f' };
  if (ct.startsWith('image/')) return { icon: <FileImageOutlined />, color: '#13c2c2' };
  if (ct.startsWith('text/') || ct === 'application/json' || ct === 'application/xml')
    return { icon: <FileTextOutlined />, color: '#595959' };
  if (ct.includes('word') || ct.includes('wordprocessingml'))
    return { icon: <FileWordOutlined />, color: '#1677ff' };
  if (ct.includes('excel') || ct.includes('spreadsheetml') || ct.includes('csv'))
    return { icon: <FileExcelOutlined />, color: '#52c41a' };
  if (ct.includes('powerpoint') || ct.includes('presentationml'))
    return { icon: <FilePptOutlined />, color: '#fa8c16' };
  if (ct.includes('zip') || ct.includes('compressed') || ct.includes('rar') || ct.includes('7z') || ct.includes('tar') || ct.includes('gzip'))
    return { icon: <FileZipOutlined />, color: '#722ed1' };

  return { icon: <FileOutlined />, color: '#8c8c8c' };
}

/** File-type icon for a MIME content type, with the raw type kept in a tooltip. */
export function FileTypeIcon({ contentType }: { contentType?: string | null }) {
  // Missing content type (e.g. a row whose metadata never resolved) → neutral icon, no misleading label.
  const ct = contentType ?? '';
  const { icon, color } = resolveIconSpec(ct);

  return (
    <Tooltip title={ct || undefined}>
      <span style={{ color, fontSize: 18 }} aria-label={ct || 'file'}>
        {icon}
      </span>
    </Tooltip>
  );
}
