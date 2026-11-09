import { useState } from 'react';
import { Table, Button, Space, Popconfirm, Tag, Typography, Alert } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { CheckOutlined, CloseOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { appMessage } from '@/core/api/notification-bridge';
import {
  usePendingEnrollments,
  useApproveEnrollment,
  useRejectEnrollment,
} from './signature-enrollment-admin-api';
import type { CertSubjectType, EnrollmentKind, PendingEnrollmentDto } from './signature-enrollment-admin-types';
import { RejectEnrollmentModal } from './reject-enrollment-modal';

const KIND_I18N_KEY: Record<EnrollmentKind, string> = {
  RemoteViettelMysign: 'signatureEnrollments.kind.remote',
  UsbTokenVgca: 'signatureEnrollments.kind.usb',
};

const SUBJECT_TYPE_TAG_COLOR: Record<CertSubjectType, string> = {
  Personal: 'default',
  Organization: 'blue',
};

/**
 * Admin review queue for self-service signing-certificate enrollments — Approve / Reject(reason).
 *
 * Tenant scope is the admin's JWT-claim tenant ONLY (H4): the BE deliberately ignores the SystemAdmin
 * X-Tenant-Id override here, so this page is NOT affected by the tenant selector — surfaced via a banner.
 * Lives on its own route (not folded into the cross-tenant signature-config page) precisely because the
 * scopes differ.
 */
export function SignatureEnrollmentAdminPage() {
  const { t } = useTranslation();
  const { toQueryParams, antPagination } = useServerPagination(20);
  const { pageNumber, pageSize } = toQueryParams();

  const { data, isFetching } = usePendingEnrollments(pageNumber, pageSize);
  const approve = useApproveEnrollment();
  const reject = useRejectEnrollment();

  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [approvingId, setApprovingId] = useState<string | null>(null);

  async function handleApprove(publicId: string) {
    setApprovingId(publicId);
    try {
      await approve.mutateAsync(publicId);
      appMessage.success(t('signatureEnrollments.approveSuccess'));
    } catch {
      appMessage.error(t('signatureEnrollments.actionError'));
    } finally {
      setApprovingId(null);
    }
  }

  async function handleReject(reason: string) {
    if (!rejectingId) return;
    try {
      await reject.mutateAsync({ publicId: rejectingId, reason });
      appMessage.success(t('signatureEnrollments.rejectSuccess'));
      setRejectingId(null);
    } catch {
      appMessage.error(t('signatureEnrollments.actionError'));
    }
  }

  const columns: ColumnsType<PendingEnrollmentDto> = [
    {
      title: t('signatureEnrollments.col.kind'),
      dataIndex: 'kind',
      key: 'kind',
      render: (kind: EnrollmentKind) => <Tag>{t(KIND_I18N_KEY[kind])}</Tag>,
    },
    {
      title: t('signatureEnrollments.col.subjectType'),
      key: 'subjectType',
      render: (_: unknown, row: PendingEnrollmentDto) => (
        <Space size={4}>
          <Tag color={SUBJECT_TYPE_TAG_COLOR[row.subjectType]}>
            {t(`signatureEnrollments.subjectType.${row.subjectType.toLowerCase()}`)}
          </Tag>
          {row.displayName && <Typography.Text type="secondary">{row.displayName}</Typography.Text>}
        </Space>
      ),
    },
    {
      title: t('signatureEnrollments.col.serial'),
      dataIndex: 'serialNumber',
      key: 'serialNumber',
      render: (serial: string) => <Typography.Text code>{serial}</Typography.Text>,
    },
    {
      title: t('signatureEnrollments.col.issuer'),
      dataIndex: 'issuer',
      key: 'issuer',
      ellipsis: true,
    },
    {
      title: t('signatureEnrollments.col.enrolledAt'),
      dataIndex: 'enrolledAt',
      key: 'enrolledAt',
      render: (iso: string) => new Date(iso).toLocaleString(),
    },
    {
      title: t('signatureEnrollments.col.actions'),
      key: 'actions',
      width: 220,
      render: (_, row) => (
        <Space>
          <Popconfirm
            title={t('signatureEnrollments.approveConfirm')}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
            onConfirm={() => handleApprove(row.publicId)}
          >
            <Button type="primary" size="small" icon={<CheckOutlined />} loading={approvingId === row.publicId}>
              {t('signatureEnrollments.approve')}
            </Button>
          </Popconfirm>
          <Button size="small" danger icon={<CloseOutlined />} onClick={() => setRejectingId(row.publicId)}>
            {t('signatureEnrollments.reject')}
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader title={t('signatureEnrollments.pageTitle')} />
      <AdminContentCard>
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          message={t('signatureEnrollments.scopeNote')}
        />
        <Table<PendingEnrollmentDto>
          rowKey="publicId"
          size="small"
          columns={columns}
          dataSource={data?.items ?? []}
          loading={isFetching}
          pagination={{ ...antPagination, total: data?.totalCount ?? 0 }}
          locale={{ emptyText: t('signatureEnrollments.empty') }}
        />
      </AdminContentCard>

      <RejectEnrollmentModal
        open={rejectingId !== null}
        confirmLoading={reject.isPending}
        onSubmit={handleReject}
        onCancel={() => setRejectingId(null)}
      />
    </div>
  );
}
