import { Tag } from 'antd';
import type { TenantStatus } from './tenant-types';

const STATUS_COLOR: Record<TenantStatus, string> = {
  Active: 'green',
  Suspended: 'orange',
  Archived: 'default',
};

const STATUS_LABEL: Record<TenantStatus, string> = {
  Active: 'Đang hoạt động',
  Suspended: 'Tạm dừng',
  Archived: 'Đã lưu trữ',
};

export function TenantStatusTag({ status }: { status: TenantStatus }) {
  return <Tag color={STATUS_COLOR[status]}>{STATUS_LABEL[status]}</Tag>;
}
