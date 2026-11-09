import { Button, Space, Popconfirm, Tag } from 'antd';
import { EditOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { AuditRetentionPolicyDto, AuditCategory } from './audit-policy-api';

// Category display labels — QĐ 742 §3.1.b five mandatory audit groups
export const CATEGORY_LABELS: Record<AuditCategory, string> = {
  AppAccess: '(i) Truy cập phần mềm',
  AdminLogin: '(ii) Đăng nhập quản trị',
  Error: '(iii) Lỗi phát sinh',
  AccountManagement: '(iv) Quản lý tài khoản',
  ConfigChange: '(v) Thay đổi cấu hình hệ thống',
};

interface UseAuditPolicyColumnsOptions {
  onEdit: (record: AuditRetentionPolicyDto) => void;
  onDelete: (record: AuditRetentionPolicyDto) => void;
  isDeleting: boolean;
}

// useAuditPolicyColumns — returns Antd ColumnsType for the audit retention policy table.
// Extracted to keep audit-policy-page.tsx under 200 LOC (modularization rule).
export function useAuditPolicyColumns({
  onEdit,
  onDelete,
  isDeleting,
}: UseAuditPolicyColumnsOptions): ColumnsType<AuditRetentionPolicyDto> {
  const { t } = useTranslation();

  return [
    {
      title: t('auditPolicy.col.category', 'Nhóm nhật ký'),
      dataIndex: 'category',
      key: 'category',
      width: 260,
      render: (v?: AuditCategory) =>
        v ? (
          <span>{CATEGORY_LABELS[v]}</span>
        ) : (
          <Tag color="blue">{t('auditPolicy.globalDefault', 'Mặc định toàn cục')}</Tag>
        ),
    },
    {
      title: t('auditPolicy.col.tenantId', 'Tenant'),
      dataIndex: 'tenantId',
      key: 'tenantId',
      width: 140,
      render: (v?: string) =>
        v ? (
          <code style={{ fontSize: 12 }}>{v.slice(0, 8)}…</code>
        ) : (
          <Tag>{t('auditPolicy.globalScope', 'Toàn hệ thống')}</Tag>
        ),
    },
    {
      title: t('auditPolicy.col.retentionDays', 'Lưu trữ (ngày)'),
      dataIndex: 'retentionDays',
      key: 'retentionDays',
      width: 140,
      render: (v: number) => (
        <span>
          {v}{' '}
          {v < 365 && (
            <Tag color="warning" style={{ marginLeft: 4 }}>
              {t('auditPolicy.belowMinimum', '< 1 năm')}
            </Tag>
          )}
        </span>
      ),
    },
    {
      title: t('auditPolicy.col.archive', 'Lưu trữ trước xóa'),
      dataIndex: 'archiveBeforePurge',
      key: 'archiveBeforePurge',
      width: 160,
      render: (v: boolean) =>
        v ? (
          <Tag color="success">{t('common.yes', 'Có')}</Tag>
        ) : (
          <Tag>{t('common.no', 'Không')}</Tag>
        ),
    },
    {
      title: t('auditPolicy.col.archivePath', 'Đường dẫn lưu trữ'),
      dataIndex: 'archivePath',
      key: 'archivePath',
      ellipsis: true,
      render: (v?: string) => v ?? '—',
    },
    {
      title: t('auditPolicy.col.updatedAt', 'Cập nhật'),
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 150,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      title: '',
      key: 'actions',
      width: 90,
      render: (_, record) => (
        <Space size="small">
          <Button size="small" icon={<EditOutlined />} onClick={() => onEdit(record)} />
          <Popconfirm
            title={t('auditPolicy.deleteConfirm', 'Xóa chính sách lưu trữ này?')}
            description={t(
              'auditPolicy.deleteWarning',
              'Nếu đây là quy tắc mặc định toàn cục, hành động này có thể ảnh hưởng đến toàn hệ thống.',
            )}
            onConfirm={() => onDelete(record)}
            okText={t('common.confirm', 'Xác nhận')}
            cancelText={t('common.cancel', 'Hủy')}
          >
            <Button danger size="small" icon={<DeleteOutlined />} loading={isDeleting} />
          </Popconfirm>
        </Space>
      ),
    },
  ];
}
