// file-field-widget.tsx — interactive File-upload field used inside form preview.
// Extracted from form-field-renderer.tsx so the renderer stays a pure switch orchestrator and
// every field-type widget lives in its own file (matches address/rich-text/table/signature widgets).
// Handles File upload integration with the Files module API (async 202 upload + background scan).

import { useEffect, useState } from 'react';
import { Form, Input, Upload, Button, Typography, Space, Progress } from 'antd';
import { UploadOutlined, LinkOutlined, DeleteOutlined } from '@ant-design/icons';
import type { UploadFile, UploadProps } from 'antd';
import type { Rule } from 'antd/es/form';
import { useTranslation } from 'react-i18next';
import { useUploadFile, useFileScanStatus, downloadFile } from '@/features/files/file-api';
import { FileLifecycleSteps, deriveFilePhase } from '@/features/files/file-lifecycle-steps';
import type { FormFieldDto } from '../form-types';
import { appMessage } from '@/core/api/notification-bridge';
import { extractApiErrorMessage } from '@/core/api/extract-api-error';

const { Text, Link } = Typography;

interface FileFieldProps {
  field: FormFieldDto;
  /** Called with fileId after successful upload */
  onUploaded: (fileId: string) => void;
  /** Validation rules for the hidden Form.Item */
  rules?: Rule[];
}

/** Parse file-specific validation rules from validationRulesJson */
function parseFileRules(field: FormFieldDto): { maxSizeMB: number; allowedExtensions: string[] } {
  if (!field.validationRulesJson) return { maxSizeMB: 10, allowedExtensions: [] };
  try {
    const vr = JSON.parse(field.validationRulesJson) as {
      maxSize?: number;
      allowedExtensions?: string[];
    };
    return {
      maxSizeMB: vr.maxSize ?? 10,
      allowedExtensions: (vr.allowedExtensions ?? []).map((e) => e.toLowerCase()),
    };
  } catch {
    return { maxSizeMB: 10, allowedExtensions: [] };
  }
}

export function FileFieldWidget({ field, onUploaded, rules }: FileFieldProps) {
  const label = field.labelVi || field.fieldKey;
  const form = Form.useFormInstance();
  const { t } = useTranslation();
  const [fileList, setFileList] = useState<UploadFile[]>([]);
  const [uploadedId, setUploadedId] = useState<string | null>(null);
  const [uploadedName, setUploadedName] = useState<string | null>(null);
  const [uploadPct, setUploadPct] = useState<number | null>(null);
  const uploadMutation = useUploadFile(setUploadPct);
  const fileRules = parseFileRules(field);
  const scanStatus = useFileScanStatus(uploadedId);

  const resetUpload = () => {
    setFileList([]);
    setUploadedId(null);
    setUploadedName(null);
    setUploadPct(null);
    form.setFieldValue(field.fieldKey, null);
  };

  // A rejected (virus-found) file is deleted server-side — drop it from the form so a tainted fileId
  // is never submitted, and prompt the user to re-upload.
  useEffect(() => {
    if (scanStatus.data?.status === 'Rejected') {
      appMessage.error(t('forms.field.fileInfected', {
        defaultValue: 'Tệp bị từ chối do nhiễm virus. Vui lòng tải tệp khác.',
      }));
      resetUpload();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scanStatus.data?.status]);

  const uploadProps: UploadProps = {
    fileList,
    beforeUpload: (file) => {
      // F5: Validate file size against per-field rules
      const sizeMB = file.size / 1024 / 1024;
      if (sizeMB > fileRules.maxSizeMB) {
        appMessage.error(t('forms.field.fileExceedsLimit', { max: fileRules.maxSizeMB }));
        return Upload.LIST_IGNORE;
      }
      // F5: Validate file extension against per-field rules
      if (fileRules.allowedExtensions.length > 0) {
        const ext = '.' + (file.name.split('.').pop()?.toLowerCase() ?? '');
        if (!fileRules.allowedExtensions.includes(ext)) {
          appMessage.error(t('forms.field.allowedTypes', { types: fileRules.allowedExtensions.join(', ') }));
          return Upload.LIST_IGNORE;
        }
      }

      const formData = new FormData();
      formData.append('file', file);
      uploadMutation.mutate(formData, {
        // POST /files is async (202): the response carries only fileId — scan runs in the background
        // and full metadata isn't ready yet. Use the locally-known file name for the uploaded display.
        onSuccess: (result) => {
          setUploadedId(result.fileId);
          setUploadedName(file.name);
          setUploadPct(null);
          form.setFieldValue(field.fieldKey, result.fileId);
          onUploaded(result.fileId);
          setFileList([{ uid: result.fileId, name: file.name, status: 'done' }]);
        },
        onError: (error) => {
          // The custom row + lifecycle stepper render nothing on a failed upload (showUploadList is
          // off, so antd's own error item is suppressed) — surface the backend reason as a toast
          // instead, falling back to a generic message for non-API rejections.
          setUploadPct(null);
          appMessage.error(
            extractApiErrorMessage(error) ??
              t('forms.field.uploadFailed', { defaultValue: 'Tải tệp thất bại. Vui lòng thử lại.' }),
          );
        },
      });
      // Prevent antd's default XHR upload
      return false;
    },
    maxCount: 1,
    onRemove: () => {
      resetUpload();
    },
  };

  return (
    <>
      {/* Hidden Form.Item registers the field so antd includes fileId in onFinish values */}
      <Form.Item name={field.fieldKey} hidden rules={rules}><Input /></Form.Item>
      {/* Visual Form.Item — no name prop (wraps Space layout, not a form control) */}
      <Form.Item key={field.id} label={label} required={field.required}>
        <Space direction="vertical" style={{ width: '100%' }}>
          {/* showUploadList=false: antd's built-in list item is suppressed because the uploaded
              file is rendered in the richer custom row below (download link + scan status + remove);
              keeping both would render the same filename twice. */}
          <Upload {...uploadProps} showUploadList={false}>
            <Button
              icon={<UploadOutlined />}
              loading={uploadMutation.isPending}
              disabled={Boolean(uploadedId)}
            >
              {uploadedId ? t('forms.field.uploaded') : t('forms.field.chooseFile')}
            </Button>
          </Upload>
          {uploadMutation.isPending && (
            <Progress percent={uploadPct ?? 0} size="small" status="active" />
          )}
          {/* Lifecycle stepper (Tải lên → Quét virus → Hoàn tất) — shows which stage the upload is at. */}
          <FileLifecycleSteps
            phase={deriveFilePhase(
              uploadMutation.isPending,
              Boolean(uploadedId),
              scanStatus.data?.status,
              scanStatus.timedOut,
            )}
          />
          {/* A stuck scan stops polling at the cap with no terminal verdict — offer a retry instead of
              an indefinitely "scanning" stepper. The fileId is kept (the scan may still resolve), so
              this only nudges; it does not clear the form value. */}
          {scanStatus.timedOut && (
            <Space>
              <Text type="warning">
                {t('forms.field.scanTimeout', {
                  defaultValue: 'Quét virus quá thời gian. Tệp vẫn đang chờ xử lý.',
                })}
              </Text>
              <Button size="small" onClick={() => scanStatus.retryScan()}>
                {t('forms.field.scanRetry', { defaultValue: 'Thử lại' })}
              </Button>
            </Space>
          )}
          {uploadedId && uploadedName && (
            <Space>
              {/* Download goes through apiClient (Bearer-authenticated blob fetch) — a raw href to
                  the [Authorize] file route would 401, so this is an onClick, not an anchor target. */}
              <Link onClick={() => downloadFile(uploadedId, uploadedName)} style={{ cursor: 'pointer' }}>
                <LinkOutlined style={{ marginRight: 4 }} />
                {uploadedName}
              </Link>
              {/* Remove control moved here since antd's list (which carried the trash icon) is hidden */}
              <Button
                type="text"
                size="small"
                danger
                icon={<DeleteOutlined />}
                onClick={resetUpload}
                aria-label={t('forms.field.removeFile', { defaultValue: 'Xóa tệp' })}
              />
            </Space>
          )}
        </Space>
      </Form.Item>
    </>
  );
}
