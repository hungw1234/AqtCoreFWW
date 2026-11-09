import { Table, Tag, Space, Typography, Tooltip } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import type {
  CertApprovalStatus,
  CertSubjectType,
  EnrollmentKind,
  SignatureEnrollmentStatusDto,
} from './signature-types';

// Maps the BE approval status to an antd Tag colour. Pending=processing(blue), Approved=success(green),
// Rejected=error(red). Centralised so the badge stays consistent across the tab.
const STATUS_TAG_COLOR: Record<CertApprovalStatus, string> = {
  Pending: 'processing',
  Approved: 'success',
  Rejected: 'error',
};

const KIND_I18N_KEY: Record<EnrollmentKind, string> = {
  RemoteViettelMysign: 'profile.signatures.kind.remote',
  UsbTokenVgca: 'profile.signatures.kind.usb',
};

// Maps the BE CertSubjectType to an antd Tag color.
const SUBJECT_TYPE_TAG_COLOR: Record<CertSubjectType, string> = {
  Personal: 'default',
  Organization: 'blue',
};

interface EnrollmentListProps {
  enrollments: SignatureEnrollmentStatusDto[];
}

/**
 * Read-only list of the current user's enrolled signing certificates with an approval-status badge.
 * Slice A is read-only — the enroll flow that creates these rows arrives in Slice B.
 */
export function EnrollmentList({ enrollments }: EnrollmentListProps) {
  const { t } = useTranslation();

  const columns: ColumnsType<SignatureEnrollmentStatusDto> = [
    {
      title: t('profile.signatures.col.kind'),
      dataIndex: 'kind',
      key: 'kind',
      render: (kind: EnrollmentKind) => t(KIND_I18N_KEY[kind]),
    },
    {
      title: t('profile.signatures.col.subjectType'),
      key: 'subjectType',
      render: (_: unknown, row: SignatureEnrollmentStatusDto) => (
        <Space size={4}>
          <Tag color={SUBJECT_TYPE_TAG_COLOR[row.subjectType]}>
            {t(`profile.signatures.subjectType.${row.subjectType.toLowerCase()}`)}
          </Tag>
          {row.displayName && <Typography.Text type="secondary">{row.displayName}</Typography.Text>}
        </Space>
      ),
    },
    {
      title: t('profile.signatures.col.status'),
      dataIndex: 'approvalStatus',
      key: 'approvalStatus',
      render: (status: CertApprovalStatus, row) => {
        const tag = (
          <Tag color={STATUS_TAG_COLOR[status]}>
            {t(`profile.signatures.status.${status.toLowerCase()}`)}
          </Tag>
        );
        // Surface the admin's rejection reason on hover so the user knows why it failed.
        return status === 'Rejected' && row.rejectionReason ? (
          <Tooltip title={row.rejectionReason}>{tag}</Tooltip>
        ) : (
          tag
        );
      },
    },
    {
      title: t('profile.signatures.col.serial'),
      dataIndex: 'serialNumber',
      key: 'serialNumber',
      render: (serial: string) => <Typography.Text code>{serial}</Typography.Text>,
    },
    {
      title: t('profile.signatures.col.issuer'),
      dataIndex: 'issuer',
      key: 'issuer',
      ellipsis: true,
    },
    {
      title: t('profile.signatures.col.enrolledAt'),
      dataIndex: 'enrolledAt',
      key: 'enrolledAt',
      render: (iso: string) => new Date(iso).toLocaleString(),
    },
  ];

  // The section title + "Đăng ký" action live in the parent tab header (so the button sits beside the title);
  // this component renders only the table.
  return (
    <Table
      rowKey="publicId"
      size="small"
      columns={columns}
      dataSource={enrollments}
      pagination={false}
      locale={{ emptyText: t('profile.signatures.enrollmentsEmpty') }}
    />
  );
}
