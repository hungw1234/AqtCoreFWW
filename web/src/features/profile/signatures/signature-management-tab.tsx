import { useState, lazy, Suspense } from 'react';
import { Button, Table, Tag, Space, Popconfirm, Card, Alert, Skeleton } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, StarFilled, StarOutlined, EditOutlined, DeleteOutlined, SafetyCertificateOutlined, PictureOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useSignatureOverview,
  useCreateSignatureProfile,
  useUpdateSignatureProfile,
  useDeleteSignatureProfile,
  useSetDefaultSignatureProfile,
} from './signatures-api';
import type { SignatureProfileDto } from './signature-types';
import { SignatureProfileFormModal, type ProfileFormMode, type ProfileFormValues } from './signature-profile-form-modal';
import { SignatureImageUploadModal } from './signature-image-upload-modal';
import { EnrollmentList } from './enrollment-list';
import { SectionHeader } from '../components/section-header';
// Lazy: the enroll modal pulls in the VGCA USB adapter graph (plugin script-loader / agent probe). Deferring it
// keeps that out of the tab's eager import graph so simply rendering the tab in a test never touches it.
const EnrollCertModal = lazy(() =>
  import('./enroll-cert-modal').then((m) => ({ default: m.EnrollCertModal })),
);

interface ModalState {
  open: boolean;
  mode: ProfileFormMode;
  editing?: SignatureProfileDto;
}

/**
 * Self-service signature management — mounted as the "Chữ ký số" tab of the profile page.
 * Sources ALL data from GET /me/signature-overview (NOT OIDC claims — those lack enrollment status).
 * Slice A scope: profile CRUD (DisplayName/default) + read-only enrollment list. Enroll, placement,
 * and image upload are later slices.
 */
export function SignatureManagementTab() {
  const { t } = useTranslation();
  const { data, isLoading, isError } = useSignatureOverview();
  const createProfile = useCreateSignatureProfile();
  const updateProfile = useUpdateSignatureProfile();
  const deleteProfile = useDeleteSignatureProfile();
  const setDefault = useSetDefaultSignatureProfile();

  const [modal, setModal] = useState<ModalState>({ open: false, mode: 'create' });
  const [enrollOpen, setEnrollOpen] = useState(false);
  // Track the image-modal target by publicId (not the row object) so the modal always sees the freshest
  // hasFullImage / hasInitialImage flags after an upload invalidates the overview query.
  const [imageProfileId, setImageProfileId] = useState<string | null>(null);
  const imageProfile = data?.profiles.find((p) => p.publicId === imageProfileId) ?? null;

  async function handleSubmit(values: ProfileFormValues) {
    try {
      if (modal.mode === 'create') {
        await createProfile.mutateAsync(values);
        appMessage.success(t('profile.signatures.createSuccess'));
      } else if (modal.editing) {
        await updateProfile.mutateAsync({ publicId: modal.editing.publicId, body: values });
        appMessage.success(t('profile.signatures.updateSuccess'));
      }
      setModal({ open: false, mode: 'create' });
    } catch {
      appMessage.error(t('profile.signatures.error'));
    }
  }

  async function handleDelete(publicId: string) {
    try {
      await deleteProfile.mutateAsync(publicId);
      appMessage.success(t('profile.signatures.deleteSuccess'));
    } catch {
      appMessage.error(t('profile.signatures.error'));
    }
  }

  async function handleSetDefault(publicId: string) {
    try {
      await setDefault.mutateAsync(publicId);
      appMessage.success(t('profile.signatures.setDefaultSuccess'));
    } catch {
      appMessage.error(t('profile.signatures.error'));
    }
  }

  const columns: ColumnsType<SignatureProfileDto> = [
    {
      title: t('profile.signatures.col.name'),
      dataIndex: 'displayName',
      key: 'displayName',
    },
    {
      title: t('profile.signatures.col.default'),
      dataIndex: 'isDefault',
      key: 'isDefault',
      render: (isDefault: boolean) =>
        isDefault ? <Tag color="gold">{t('profile.signatures.default')}</Tag> : null,
    },
    // NOTE: the "Certificate linked" column is intentionally hidden — profile↔cert linking
    // (UserSignatureProfile.SignerCertEnrollmentId / LinkCert) is not yet wired to any API/UI, and
    // linkage is NOT required to sign (the binding guard matches the signing cert to an Approved
    // enrollment by serial/issuer/thumbprint at sign time). Re-introduce this column when the
    // link-cert slice ships. `hasCertLinked` stays on the DTO for that future use.
    {
      title: t('profile.signatures.col.images'),
      key: 'images',
      render: (_, row) => (
        <Space size={4}>
          <Tag color={row.hasFullImage ? 'green' : 'default'}>{t('profile.signatures.image.full')}</Tag>
          <Tag color={row.hasInitialImage ? 'green' : 'default'}>{t('profile.signatures.image.initial')}</Tag>
        </Space>
      ),
    },
    {
      title: t('profile.signatures.col.actions'),
      key: 'actions',
      render: (_, row) => (
        <Space>
          <Button
            type="text"
            size="small"
            icon={row.isDefault ? <StarFilled /> : <StarOutlined />}
            disabled={row.isDefault || setDefault.isPending}
            onClick={() => handleSetDefault(row.publicId)}
          >
            {t('profile.signatures.setDefault')}
          </Button>
          <Button
            type="text"
            size="small"
            icon={<PictureOutlined />}
            onClick={() => setImageProfileId(row.publicId)}
          >
            {t('profile.signatures.image.rowAction')}
          </Button>
          <Button
            type="text"
            size="small"
            icon={<EditOutlined />}
            onClick={() => setModal({ open: true, mode: 'edit', editing: row })}
          >
            {t('profile.signatures.edit')}
          </Button>
          <Popconfirm
            title={t('profile.signatures.deleteConfirm')}
            okText={t('common.confirm')}
            cancelText={t('common.cancel')}
            onConfirm={() => handleDelete(row.publicId)}
          >
            <Button type="text" size="small" danger icon={<DeleteOutlined />}>
              {t('profile.signatures.delete')}
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  if (isLoading) return <Skeleton active />;
  if (isError) return <Alert type="error" message={t('profile.signatures.loadError')} showIcon />;

  return (
    <Space direction="vertical" size="large" style={{ width: '100%' }}>
      <Card variant="borderless" style={{ boxShadow: 'var(--elevation-1)' }}>
        <SectionHeader
          title={t('profile.signatures.profilesTitle')}
          description={t('profile.signatures.profilesHint')}
          extra={
            <Button
              type="primary"
              icon={<PlusOutlined />}
              onClick={() => setModal({ open: true, mode: 'create' })}
            >
              {t('profile.signatures.addProfile')}
            </Button>
          }
        />
        <Table
          rowKey="publicId"
          size="small"
          columns={columns}
          dataSource={data?.profiles ?? []}
          pagination={false}
          locale={{ emptyText: t('profile.signatures.empty') }}
          style={{ marginTop: 16 }}
        />
      </Card>

      <Card variant="borderless" style={{ boxShadow: 'var(--elevation-1)' }}>
        <SectionHeader
          title={t('profile.signatures.enrollmentsTitle', 'Chứng thư số')}
          description={t('profile.signatures.enrollmentsHint')}
          extra={
            <Button icon={<SafetyCertificateOutlined />} onClick={() => setEnrollOpen(true)}>
              {t('profile.signatures.enroll.openButton', 'Đăng ký chứng thư số')}
            </Button>
          }
        />
        <div style={{ marginTop: 16 }}>
          <EnrollmentList enrollments={data?.enrollments ?? []} />
        </div>
      </Card>

      {enrollOpen && (
        <Suspense fallback={null}>
          <EnrollCertModal
            open={enrollOpen}
            nationalIdEstablished={data?.nationalIdEstablished ?? false}
            onClose={() => setEnrollOpen(false)}
          />
        </Suspense>
      )}

      <SignatureProfileFormModal
        open={modal.open}
        mode={modal.mode}
        initialValues={
          modal.editing
            ? {
                displayName: modal.editing.displayName,
                showSerial: modal.editing.showSerial,
                showIssuer: modal.editing.showIssuer,
              }
            : undefined
        }
        confirmLoading={createProfile.isPending || updateProfile.isPending}
        onSubmit={handleSubmit}
        onCancel={() => setModal({ open: false, mode: 'create' })}
      />

      <SignatureImageUploadModal
        open={imageProfile !== null}
        profile={imageProfile}
        onClose={() => setImageProfileId(null)}
      />
    </Space>
  );
}
