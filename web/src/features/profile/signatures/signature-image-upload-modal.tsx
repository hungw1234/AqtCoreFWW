// signature-image-upload-modal.tsx — per-profile visual signature image manager.
// Each of the two slots (full signature / initial paraph) can be filled either by uploading a PNG/JPEG
// or by drawing on a canvas (reusing the shared SignatureWidget). Both paths end at the same multipart
// endpoint — a drawn signature is exported to a PNG File before upload, so the wire shape is identical.
import { useEffect, useState } from 'react';
import { Modal, Segmented, Upload, Button, Card, Tag, Space, Row, Col, Typography, Image } from 'antd';
import { UploadOutlined, HighlightOutlined, CheckCircleFilled } from '@ant-design/icons';
import type { RcFile } from 'antd/es/upload';
import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import { SignatureWidget } from '@/shared/components/signature-widget';
import { useUploadSignatureImage, fetchSignatureImageBlob } from './signatures-api';
import type { SignatureProfileDto, SignatureVisualType } from './signature-types';

// Keep in lockstep with the BE UploadSignatureImageCommandValidator.MaxImageBytes cap. The BE re-validates,
// so this is a UX pre-check (fail fast with a clear message) — not the security boundary.
const MAX_IMAGE_BYTES = 500 * 1024;
const ACCEPTED_MIME = ['image/png', 'image/jpeg'];

type SlotMode = 'upload' | 'draw';

/** Decodes a base64 data URL (from the drawing canvas) into a File so it rides the same multipart path. */
function dataUrlToFile(dataUrl: string, filename: string): File {
  const [meta, b64] = dataUrl.split(',');
  const mime = /:(.*?);/.exec(meta)?.[1] ?? 'image/png';
  const binary = atob(b64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return new File([bytes], filename, { type: mime });
}

interface ImageSlotProps {
  publicId: string;
  visualType: SignatureVisualType;
  label: string;
  hasImage: boolean;
}

/** One image slot: a mode toggle over an Upload picker and a draw canvas, plus a Save that posts the result. */
function ImageSlot({ publicId, visualType, label, hasImage }: ImageSlotProps) {
  const { t } = useTranslation();
  const upload = useUploadSignatureImage();
  const [mode, setMode] = useState<SlotMode>('upload');
  const [picked, setPicked] = useState<File | null>(null);
  const [drawn, setDrawn] = useState('');
  // Object URL of the currently-stored image for the preview. reloadKey bumps after a successful upload so
  // the preview refetches the new bytes (hasImage may already be true on a re-upload → the flag alone won't
  // retrigger the effect).
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  // Fetch + render the stored image when the slot is filled; revoke the object URL on cleanup/change so we
  // never leak blobs. Only fires when hasImage is true — the overview is the source of truth for presence.
  useEffect(() => {
    if (!hasImage) {
      setPreviewUrl(null);
      return;
    }
    let cancelled = false;
    let objectUrl: string | null = null;
    fetchSignatureImageBlob(publicId, visualType)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setPreviewUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setPreviewUrl(null);
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [publicId, visualType, hasImage, reloadKey]);

  // beforeUpload returns false (never auto-upload) once the file passes the UX pre-check; LIST_IGNORE drops
  // an invalid pick so it never lingers in the list.
  function beforeUpload(file: RcFile) {
    if (!ACCEPTED_MIME.includes(file.type)) {
      appMessage.error(t('profile.signatures.image.invalidType'));
      return Upload.LIST_IGNORE;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      appMessage.error(t('profile.signatures.image.tooLarge'));
      return Upload.LIST_IGNORE;
    }
    setPicked(file);
    return false;
  }

  const pendingFile = mode === 'upload' ? picked : drawn ? dataUrlToFile(drawn, `${visualType}.png`) : null;

  async function handleSave() {
    if (!pendingFile) {
      appMessage.warning(t('profile.signatures.image.noImage'));
      return;
    }
    try {
      await upload.mutateAsync({ publicId, visualType, file: pendingFile });
      appMessage.success(t('profile.signatures.image.saveSuccess'));
      setPicked(null);
      setDrawn('');
      setReloadKey((k) => k + 1); // refetch the preview so it reflects the just-uploaded image
    } catch {
      appMessage.error(t('profile.signatures.image.error'));
    }
  }

  return (
    <Card
      size="small"
      title={
        <Space>
          {label}
          {hasImage ? (
            <Tag icon={<CheckCircleFilled />} color="success">
              {t('profile.signatures.image.has')}
            </Tag>
          ) : (
            <Tag>{t('profile.signatures.image.none')}</Tag>
          )}
        </Space>
      }
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        {previewUrl && (
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('profile.signatures.image.current')}
            </Typography.Text>
            <div
              style={{
                marginTop: 4,
                padding: 8,
                background: 'var(--gov-bg-muted)',
                borderRadius: 'var(--radius-sm)',
                textAlign: 'center',
              }}
            >
              <Image
                src={previewUrl}
                alt={label}
                style={{ maxHeight: 96, maxWidth: '100%', objectFit: 'contain' }}
              />
            </div>
          </div>
        )}

        <Segmented
          block
          value={mode}
          onChange={(v) => setMode(v as SlotMode)}
          options={[
            { label: t('profile.signatures.image.modeUpload'), value: 'upload', icon: <UploadOutlined /> },
            { label: t('profile.signatures.image.modeDraw'), value: 'draw', icon: <HighlightOutlined /> },
          ]}
        />

        {mode === 'upload' ? (
          <Space direction="vertical">
            <Upload
              accept="image/png,image/jpeg"
              maxCount={1}
              beforeUpload={beforeUpload}
              onRemove={() => setPicked(null)}
              fileList={picked ? [{ uid: '-1', name: picked.name, status: 'done' }] : []}
            >
              <Button icon={<UploadOutlined />}>{t('profile.signatures.image.pick')}</Button>
            </Upload>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('profile.signatures.image.hint')}
            </Typography.Text>
          </Space>
        ) : (
          <SignatureWidget value={drawn} onChange={setDrawn} />
        )}

        <Button
          type="primary"
          block
          loading={upload.isPending}
          disabled={!pendingFile}
          onClick={handleSave}
        >
          {t('profile.signatures.image.save')}
        </Button>
      </Space>
    </Card>
  );
}

interface SignatureImageUploadModalProps {
  open: boolean;
  /** The profile whose image slots are being managed; null renders nothing (modal closed / no target). */
  profile: SignatureProfileDto | null;
  onClose: () => void;
}

/**
 * Modal shell hosting the two image slots for a single profile. Uploads invalidate the signature overview,
 * so the parent tab's per-slot badges refresh live while this modal stays open for the other slot.
 */
export function SignatureImageUploadModal({ open, profile, onClose }: SignatureImageUploadModalProps) {
  const { t } = useTranslation();
  if (!profile) return null;

  return (
    <Modal
      open={open}
      title={t('profile.signatures.image.title', { name: profile.displayName })}
      onCancel={onClose}
      footer={<Button onClick={onClose}>{t('profile.signatures.image.close')}</Button>}
      width={720}
      destroyOnClose
    >
      <Row gutter={[16, 16]}>
        <Col xs={24} sm={12}>
          <ImageSlot
            publicId={profile.publicId}
            visualType="FullSignature"
            label={t('profile.signatures.image.full')}
            hasImage={profile.hasFullImage}
          />
        </Col>
        <Col xs={24} sm={12}>
          <ImageSlot
            publicId={profile.publicId}
            visualType="InitialSignature"
            label={t('profile.signatures.image.initial')}
            hasImage={profile.hasInitialImage}
          />
        </Col>
      </Row>
    </Modal>
  );
}
