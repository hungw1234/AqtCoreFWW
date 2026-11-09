/**
 * signed-pdf-toolbar.tsx — Controls bar for the embedded signed-PDF viewer.
 *
 * Stateless/presentational: zoom (out / level / in / fit-width), page indicator, and in-document
 * search (term input + match counter + prev/next). All state lives in the parent viewer so the
 * toolbar stays trivially testable and reusable.
 */

import { Button, Space, Input, Tooltip, Typography } from 'antd';
import {
  ZoomInOutlined,
  ZoomOutOutlined,
  ColumnWidthOutlined,
  SearchOutlined,
  UpOutlined,
  DownOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

export interface SignedPdfToolbarProps {
  scalePercent: number;
  zoomMinReached: boolean;
  zoomMaxReached: boolean;
  onZoomIn: () => void;
  onZoomOut: () => void;
  onFitWidth: () => void;
  currentPage: number;
  numPages: number;
  searchTerm: string;
  onSearchChange: (value: string) => void;
  matchCount: number;
  activeMatch: number;
  onNextMatch: () => void;
  onPrevMatch: () => void;
}

export function SignedPdfToolbar(props: SignedPdfToolbarProps) {
  const { t } = useTranslation();
  const hasMatches = props.matchCount > 0;

  return (
    <Space
      style={{
        width: '100%',
        justifyContent: 'space-between',
        padding: '6px 8px',
        background: '#fafafa',
        borderRadius: 6,
        border: '1px solid #f0f0f0',
      }}
      wrap
    >
      {/* Zoom cluster */}
      <Space size={4}>
        <Tooltip title={t('digitalSignature.signedDocument.zoomOut', 'Thu nhỏ')}>
          <Button
            size="small"
            icon={<ZoomOutOutlined />}
            disabled={props.zoomMinReached}
            onClick={props.onZoomOut}
          />
        </Tooltip>
        <Text style={{ width: 46, textAlign: 'center' }}>{props.scalePercent}%</Text>
        <Tooltip title={t('digitalSignature.signedDocument.zoomIn', 'Phóng to')}>
          <Button
            size="small"
            icon={<ZoomInOutlined />}
            disabled={props.zoomMaxReached}
            onClick={props.onZoomIn}
          />
        </Tooltip>
        <Tooltip title={t('digitalSignature.signedDocument.fitWidth', 'Khít bề ngang')}>
          <Button size="small" icon={<ColumnWidthOutlined />} onClick={props.onFitWidth} />
        </Tooltip>
      </Space>

      {/* Page indicator */}
      <Text type="secondary">
        {props.numPages > 0
          ? `${t('digitalSignature.signedDocument.page', 'Trang')} ${props.currentPage} / ${props.numPages}`
          : '—'}
      </Text>

      {/* Search cluster */}
      <Space size={4}>
        <Input
          size="small"
          allowClear
          prefix={<SearchOutlined />}
          placeholder={t('digitalSignature.signedDocument.searchPlaceholder', 'Tìm trong tài liệu')}
          value={props.searchTerm}
          onChange={(e) => props.onSearchChange(e.target.value)}
          onPressEnter={props.onNextMatch}
          style={{ width: 190 }}
        />
        <Text type="secondary" style={{ minWidth: 52, textAlign: 'center' }}>
          {props.searchTerm ? `${props.activeMatch}/${props.matchCount}` : ''}
        </Text>
        <Button
          size="small"
          icon={<UpOutlined />}
          disabled={!hasMatches}
          onClick={props.onPrevMatch}
        />
        <Button
          size="small"
          icon={<DownOutlined />}
          disabled={!hasMatches}
          onClick={props.onNextMatch}
        />
      </Space>
    </Space>
  );
}
