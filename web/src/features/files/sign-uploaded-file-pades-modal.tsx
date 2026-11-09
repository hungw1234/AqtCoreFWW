/**
 * sign-uploaded-file-pades-modal.tsx — production modal to sign an uploaded PDF via the REMOTE PAdES path
 * (Viettel Mysign) WITH an optional visible-stamp placement.
 *
 * Flow: preview the PDF (fetchFileBlob) → optionally draw the stamp rectangle (SignaturePlacementOverlay,
 * PDF-point coordinates) → pick a signature profile + visual slot (full / paraph) → start the remote signing
 * request (OTP fires on the signer's phone) → poll until Completed → view / download the signed PDF.
 *
 * Gates:
 *  - Approved enrollment (overview.hasApprovedEnrollment): a Mysign cert must be approved before signing.
 *  - At least one signature profile must exist (created in the profile → "Chữ ký số" tab).
 *
 * Placement is OPTIONAL: with no rectangle the BE signs invisibly (backward-compatible). When a rectangle is
 * drawn, the chosen profile MUST have an uploaded signature image for the selected slot, otherwise the BE
 * rejects the request — that error is surfaced here (the image-upload UI ships separately, M2).
 */

import { useState, useEffect, useMemo, useCallback } from 'react';
import { Modal, Alert, Button, Space, Form, Select, Input, Spin, Typography, Result } from 'antd';
import { SafetyCertificateOutlined, EyeOutlined, DownloadOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useSignatureOverview } from '@/features/profile/signatures/signatures-api';
import { useSigningRequestStatus, downloadSignedPdf } from '@/features/digital-signature/signature-api';
import { SignaturePlacementOverlay, type PlacementSelection } from '@/features/digital-signature/placement/signature-placement-overlay';
import { buildPlacementJson, type SignatureVisualType } from '@/features/digital-signature/placement/placement-geometry';
import { SignedPdfViewer } from '@/features/digital-signature/signed-document/signed-pdf-viewer';
import { useFileBlob } from './file-api';
import { useSignUploadedFilePades } from './use-sign-uploaded-file-pades';

const { Text } = Typography;

export interface SignUploadedFilePadesModalProps {
  open: boolean;
  fileId: string;
  originalFileName: string;
  onClose: () => void;
}

export function SignUploadedFilePadesModal({
  open,
  fileId,
  originalFileName,
  onClose,
}: SignUploadedFilePadesModalProps) {
  const { t } = useTranslation();

  const { data: overview, isLoading: overviewLoading } = useSignatureOverview(open);
  // Only prefetch the (full) PDF preview once the sign gates pass — no point downloading bytes the user
  // cannot act on (L2). hasApprovedEnrollment + at least one profile are the same gates that render the picker.
  const canPreview =
    open && (overview?.hasApprovedEnrollment ?? false) && (overview?.profiles?.length ?? 0) > 0;
  const { data: blob } = useFileBlob(fileId, canPreview);

  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [selectedProfileId, setSelectedProfileId] = useState<string | undefined>(undefined);
  const [visualType, setVisualType] = useState<SignatureVisualType>('full');
  // User-entered "Lý do" rendered on the stamp; blank → the BE omits the reason line.
  const [reason, setReason] = useState('');
  const [placement, setPlacement] = useState<PlacementSelection | null>(null);
  const [signedBlob, setSignedBlob] = useState<Blob | null>(null);
  const [fetchFailed, setFetchFailed] = useState(false);
  const [viewerOpen, setViewerOpen] = useState(false);

  const { isLoading, error, requestId, startSigning, reset } = useSignUploadedFilePades();
  const statusQuery = useSigningRequestStatus(requestId);
  const signingStatus = statusQuery.data?.status;

  // Own the preview objectURL lifecycle (mirror signed-pdf-viewer: revoke on blob change / unmount).
  useEffect(() => {
    if (!blob) {
      setObjectUrl(null);
      return;
    }
    const url = URL.createObjectURL(blob);
    setObjectUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [blob]);

  // Default the profile to the user's default (else first) once the overview loads.
  useEffect(() => {
    if (!open) return;
    const profiles = overview?.profiles ?? [];
    setSelectedProfileId((prev) => prev ?? profiles.find((p) => p.isDefault)?.publicId ?? profiles[0]?.publicId);
  }, [open, overview]);

  // Reset per-run state when the modal opens.
  useEffect(() => {
    if (open) {
      setVisualType('full');
      setReason('');
      setPlacement(null);
      setSignedBlob(null);
      setFetchFailed(false);
      setViewerOpen(false);
      reset();
    }
  }, [open, reset]);

  // Once the request completes, fetch the assembled signed PDF so View/Download are ready (no auto-download).
  // A fetch failure here is non-fatal (the sign itself succeeded) but must be surfaced with a retry (M1) so the
  // user is not left on a success screen with no way to retrieve the file.
  useEffect(() => {
    let cancelled = false;
    if (signingStatus === 'Completed' && requestId && !signedBlob) {
      downloadSignedPdf(requestId)
        .then((b) => { if (!cancelled) { setSignedBlob(b); setFetchFailed(false); } })
        .catch(() => { if (!cancelled) setFetchFailed(true); });
    }
    return () => { cancelled = true; };
  }, [signingStatus, requestId, signedBlob]);

  const retryFetch = useCallback(() => {
    if (!requestId) return;
    setFetchFailed(false);
    downloadSignedPdf(requestId)
      .then(setSignedBlob)
      .catch(() => setFetchFailed(true));
  }, [requestId]);

  const profiles = overview?.profiles ?? [];
  const hasApproved = overview?.hasApprovedEnrollment ?? false;

  const isCompleted = signingStatus === 'Completed';
  const isFailed = signingStatus === 'Failed' || signingStatus === 'Expired';
  const ceremonyDone = isCompleted || isFailed || statusQuery.timedOut;

  const canSign = hasApproved && !!selectedProfileId && !isLoading && !requestId;

  const placementJson = useMemo(() => {
    if (!placement || !selectedProfileId) return null;
    return buildPlacementJson({
      page: placement.page,
      x: placement.rect.x,
      y: placement.rect.y,
      w: placement.rect.w,
      h: placement.rect.h,
      profilePublicId: selectedProfileId,
      visualType,
    });
  }, [placement, selectedProfileId, visualType]);

  const handleSign = useCallback(async () => {
    if (!canSign) return;
    await startSigning({
      fileId,
      documentName: originalFileName,
      reason,
      placement: placementJson,
    });
  }, [canSign, startSigning, fileId, originalFileName, reason, placementJson]);

  return (
    <Modal
      open={open}
      title={<Space><SafetyCertificateOutlined />{t('digitalSignature.placement.title', 'Ký số có đóng dấu (PAdES remote)')}</Space>}
      onCancel={onClose}
      onOk={handleSign}
      okText={t('digitalSignature.sign', 'Ký số')}
      cancelText={t('common.close', 'Đóng')}
      okButtonProps={{ disabled: !canSign, loading: isLoading }}
      footer={ceremonyDone ? <Button type="primary" onClick={onClose}>{t('common.close', 'Đóng')}</Button> : undefined}
      destroyOnHidden
      width={760}
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        {overviewLoading && (
          <Space><Spin size="small" /><Text type="secondary">{t('common.loading', 'Đang tải...')}</Text></Space>
        )}

        {/* Approved-enrollment gate — no signing without an approved Mysign certificate. */}
        {!overviewLoading && !hasApproved && (
          <Alert
            type="warning"
            showIcon
            message={t('digitalSignature.placement.notApprovedTitle', 'Chưa có chứng thư số được duyệt')}
            description={t(
              'digitalSignature.placement.notApprovedDesc',
              'Bạn cần đăng ký và được duyệt một chứng thư số ký từ xa (Mysign) trước khi ký. Vào trang cá nhân → tab "Chữ ký số".',
            )}
          />
        )}

        {/* No profile to stamp with. */}
        {!overviewLoading && hasApproved && profiles.length === 0 && (
          <Alert
            type="warning"
            showIcon
            message={t('digitalSignature.placement.noProfileTitle', 'Chưa có hồ sơ chữ ký')}
            description={t(
              'digitalSignature.placement.noProfileDesc',
              'Hãy tạo một hồ sơ chữ ký trong trang cá nhân → tab "Chữ ký số" trước khi đóng dấu.',
            )}
          />
        )}

        {/* Picker + form — only before the ceremony starts and when the gates pass. */}
        {hasApproved && profiles.length > 0 && !requestId && (
          <>
            <Form layout="vertical" size="small" style={{ marginBottom: 0 }}>
              <Form.Item label={t('digitalSignature.placement.profile', 'Hồ sơ chữ ký')} style={{ marginBottom: 8 }}>
                <Select
                  value={selectedProfileId}
                  onChange={setSelectedProfileId}
                  options={profiles.map((p) => ({
                    value: p.publicId,
                    label: p.isDefault ? `${p.displayName} ★` : p.displayName,
                  }))}
                />
              </Form.Item>
              <Form.Item label={t('digitalSignature.placement.visualType', 'Kiểu chữ ký')} style={{ marginBottom: 8 }}>
                <Select<SignatureVisualType>
                  value={visualType}
                  onChange={setVisualType}
                  options={[
                    { value: 'full', label: t('digitalSignature.placement.visualFull', 'Chữ ký đầy đủ') },
                    { value: 'initial', label: t('digitalSignature.placement.visualInitial', 'Chữ ký nháy') },
                  ]}
                />
              </Form.Item>
              <Form.Item label={t('digitalSignature.placement.reason', 'Lý do ký')} style={{ marginBottom: 8 }}>
                <Input
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  maxLength={80}
                  allowClear
                  placeholder={t('digitalSignature.placement.reasonPlaceholder', 'VD: Phê duyệt văn bản (để trống nếu không hiển thị)')}
                />
              </Form.Item>
            </Form>

            <Alert
              type="info"
              showIcon
              message={
                placement
                  ? t('digitalSignature.placement.willStamp', 'Sẽ đóng dấu chữ ký tại vị trí đã chọn (trang {{page}}).', { page: placement.page })
                  : t('digitalSignature.placement.noRectHint', 'Chưa chọn vị trí — tài liệu sẽ được ký vô hình. Kéo chuột trên trang để đóng dấu chữ ký.')
              }
            />

            <SignaturePlacementOverlay fileUrl={objectUrl} onChange={setPlacement} />
          </>
        )}

        {isLoading && (
          <Space><Spin size="small" /><Text type="secondary">{t('digitalSignature.signingInProgress', 'Đang thực hiện ký số...')}</Text></Space>
        )}

        {requestId && !isLoading && !ceremonyDone && (
          <Space><Spin size="small" /><Text type="secondary">{t('digitalSignature.waitingCompletion', 'Đang chờ xác nhận hoàn tất...')}</Text></Space>
        )}

        {statusQuery.timedOut && (
          <Alert
            type="warning"
            showIcon
            message={t('digitalSignature.pollTimedOut', 'Hết thời gian chờ')}
            description={
              <Space direction="vertical" size="small">
                <span>{t('digitalSignature.pollTimedOutDesc', 'Không nhận được xác nhận hoàn tất từ máy chủ. Vui lòng kiểm tra lại danh sách tệp.')}</span>
                <Button size="small" onClick={() => statusQuery.retryPoll()}>{t('common.retry', 'Thử lại')}</Button>
              </Space>
            }
          />
        )}

        {isCompleted && (
          <Result
            status="success"
            title={t('digitalSignature.success', 'Ký số thành công')}
            subTitle={t('digitalSignature.uploadedSignedDesc', 'Tệp PDF đã được ký số. Danh sách tệp sẽ được cập nhật.')}
            extra={
              signedBlob ? (
                <Space>
                  <Button icon={<EyeOutlined />} onClick={() => setViewerOpen(true)}>{t('common.view', 'Xem')}</Button>
                  <Button type="primary" icon={<DownloadOutlined />} onClick={() => {
                    const url = URL.createObjectURL(signedBlob);
                    const a = document.createElement('a');
                    a.href = url;
                    a.download = originalFileName;
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                    URL.revokeObjectURL(url);
                  }}>{t('common.download', 'Tải xuống')}</Button>
                </Space>
              ) : fetchFailed ? (
                <Space direction="vertical" size="small">
                  <Text type="secondary">{t('digitalSignature.placement.fetchFailed', 'Đã ký nhưng chưa tải được PDF đã ký. Vui lòng thử lại.')}</Text>
                  <Button onClick={retryFetch}>{t('common.retry', 'Thử lại')}</Button>
                </Space>
              ) : (
                <Space><Spin size="small" /><Text type="secondary">{t('digitalSignature.placement.fetchingSigned', 'Đang tải PDF đã ký...')}</Text></Space>
              )
            }
          />
        )}

        {isFailed && (
          <Alert
            type="error"
            showIcon
            message={t('digitalSignature.failed', 'Ký số thất bại')}
            description={statusQuery.data?.failureReason ?? t('digitalSignature.unknownFailure', 'Không xác định được nguyên nhân lỗi.')}
          />
        )}

        {/* Hook-level error (e.g. BE profile-image-missing on a visible stamp) — already a message string;
            suppressed once the durable status reports a terminal state. */}
        {error && !isFailed && !isCompleted && (
          <Alert type="error" showIcon message={t('digitalSignature.failed', 'Ký số thất bại')} description={error} />
        )}
      </Space>

      <SignedPdfViewer open={viewerOpen} blob={signedBlob} title={originalFileName} onClose={() => setViewerOpen(false)} />
    </Modal>
  );
}
