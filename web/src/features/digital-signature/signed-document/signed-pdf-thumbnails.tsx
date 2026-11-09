/**
 * signed-pdf-thumbnails.tsx — Page-thumbnail sidebar for the embedded signed-PDF viewer.
 *
 * Renders one react-pdf <Thumbnail> per page. MUST be mounted inside the same <Document> as the
 * main page list — Thumbnail reads the loaded PDF from react-pdf's Document context, so a separate
 * Document would re-fetch/re-parse the file. The active page is highlighted to mirror scroll
 * position; clicking a thumbnail asks the parent to scroll the matching page into view.
 */

import { Thumbnail } from 'react-pdf';
import { Typography } from 'antd';

const { Text } = Typography;

export interface SignedPdfThumbnailsProps {
  numPages: number;
  currentPage: number;
  onSelect: (page: number) => void;
}

export function SignedPdfThumbnails({ numPages, currentPage, onSelect }: SignedPdfThumbnailsProps) {
  if (numPages <= 0) return null;

  return (
    <>
      {Array.from({ length: numPages }, (_, i) => i + 1).map((page) => (
        <div
          key={page}
          data-testid={`pdf-thumb-${page}`}
          className={`signed-pdf-thumb${page === currentPage ? ' active' : ''}`}
          onClick={() => onSelect(page)}
        >
          <Thumbnail pageNumber={page} width={104} onItemClick={({ pageNumber }) => onSelect(pageNumber)} />
          <Text type="secondary" className="signed-pdf-thumb-label">
            {page}
          </Text>
        </div>
      ))}
    </>
  );
}
