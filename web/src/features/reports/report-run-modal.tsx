import { useState } from 'react';
import { Modal, Form, Select, Radio, Input, Button, Alert, Space } from 'antd';
import { PlayCircleOutlined } from '@ant-design/icons';
import { useRunReport } from './report-api';
import type { ReportDefinitionDto, OutputFormat } from './report-types';

interface ReportRunModalProps {
  open: boolean;
  definitions: ReportDefinitionDto[];
  onClose: () => void;
  /** Called with the new executionId so parent can start polling */
  onExecutionStarted: (executionId: string) => void;
}

interface FormValues {
  reportDefinitionId: string;
  formatOverride: OutputFormat;
  parametersJson: string;
}

// ReportRunModal — select a report definition, fill params, submit to /reports/run
export function ReportRunModal({ open, definitions, onClose, onExecutionStarted }: ReportRunModalProps) {
  const [form] = Form.useForm<FormValues>();
  const [error, setError] = useState<string | null>(null);
  const { mutate: runReport, isPending } = useRunReport();

  function handleFinish(values: FormValues) {
    setError(null);

    // Validate parametersJson is valid JSON (or empty)
    const paramsJson = values.parametersJson?.trim() || '{}';
    try {
      JSON.parse(paramsJson);
    } catch {
      setError('Tham số không đúng định dạng JSON.');
      return;
    }

    runReport(
      {
        reportDefinitionId: values.reportDefinitionId,
        parametersJson: paramsJson,
        formatOverride: values.formatOverride,
      },
      {
        onSuccess: (executionId) => {
          form.resetFields();
          onClose();
          // executionId returned from 202 response body
          if (executionId) onExecutionStarted(String(executionId));
        },
        onError: (err: unknown) => {
          const msg = err instanceof Error ? err.message : 'Không thể chạy báo cáo.';
          setError(msg);
        },
      },
    );
  }

  function handleCancel() {
    form.resetFields();
    setError(null);
    onClose();
  }

  // Get parametersSchema for selected definition to show hint
  const selectedId = Form.useWatch('reportDefinitionId', form);
  const selectedDef = definitions.find((d) => d.id === selectedId);

  return (
    <Modal
      title="Chạy báo cáo"
      open={open}
      onCancel={handleCancel}
      footer={null}
      destroyOnHidden
      width={560}
    >
      {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} />}

      <Form form={form} layout="vertical" onFinish={handleFinish} initialValues={{ formatOverride: 'Excel' }}>
        <Form.Item
          name="reportDefinitionId"
          label="Mẫu báo cáo"
          rules={[{ required: true, message: 'Vui lòng chọn mẫu báo cáo' }]}
        >
          <Select
            showSearch
            placeholder="Chọn mẫu báo cáo..."
            optionFilterProp="label"
            options={definitions
              .filter((d) => d.isActive)
              .map((d) => ({ value: d.id, label: d.nameVi || d.name }))}
          />
        </Form.Item>

        <Form.Item name="formatOverride" label="Định dạng xuất">
          <Radio.Group>
            <Radio value="Excel">Excel (.xlsx)</Radio>
            <Radio value="Pdf">PDF</Radio>
          </Radio.Group>
        </Form.Item>

        <Form.Item
          name="parametersJson"
          label="Tham số (JSON)"
          tooltip="Để trống nếu không cần tham số. Ví dụ: {&quot;fromDate&quot;: &quot;2024-01-01&quot;}"
          extra={
            selectedDef?.parametersSchema
              ? <span style={{ fontSize: 12, color: '#5A6A7A' }}>Schema: {selectedDef.parametersSchema}</span>
              : null
          }
        >
          <Input.TextArea
            rows={3}
            placeholder='{}'
            style={{ fontFamily: 'monospace' }}
          />
        </Form.Item>

        <Form.Item style={{ marginBottom: 0 }}>
          <Space>
            <Button
              type="primary"
              htmlType="submit"
              icon={<PlayCircleOutlined />}
              loading={isPending}
            >
              Chạy báo cáo
            </Button>
            <Button onClick={handleCancel}>Hủy</Button>
          </Space>
        </Form.Item>
      </Form>
    </Modal>
  );
}
