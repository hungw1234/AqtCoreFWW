import { useState } from 'react';
import { Modal, Form, Input, Select, DatePicker, Alert, Button, Typography } from 'antd';
import { CopyOutlined, CheckOutlined } from '@ant-design/icons';
import { useCreateApiKey } from './api-key-api';
import { API_SCOPES } from './api-key-types';
import type { CreateApiKeyRequest } from './api-key-types';
import { appMessage } from '@/core/api/notification-bridge';

const { Text } = Typography;

interface ApiKeyCreateModalProps {
  open: boolean;
  onClose: () => void;
}

// ApiKeyCreateModal — create API key form; displays full key ONCE after creation
export function ApiKeyCreateModal({ open, onClose }: ApiKeyCreateModalProps) {
  const [form] = Form.useForm<CreateApiKeyRequest & { expiryDate?: import('dayjs').Dayjs }>();
  const [generatedKey, setGeneratedKey] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const createMutation = useCreateApiKey();

  async function handleCreate() {
    try {
      const values = await form.validateFields();
      const body: CreateApiKeyRequest = {
        name: values.name,
        scopes: values.scopes,
        expiresAt: values.expiryDate?.toISOString(),
      };
      const result = await createMutation.mutateAsync(body);
      setGeneratedKey(result.plainTextKey);
    } catch {
      // validation or API error handled inline
    }
  }

  async function handleCopy() {
    if (!generatedKey) return;
    await navigator.clipboard.writeText(generatedKey);
    setCopied(true);
    appMessage.success('Đã sao chép API key');
    setTimeout(() => setCopied(false), 3000);
  }

  function handleClose() {
    setGeneratedKey(null);
    setCopied(false);
    form.resetFields();
    onClose();
  }

  const scopeOptions = API_SCOPES.map((s) => ({ label: s, value: s }));

  return (
    <Modal
      title="Tạo API Key mới"
      open={open}
      onCancel={handleClose}
      footer={
        generatedKey ? (
          <Button onClick={handleClose}>Đóng</Button>
        ) : (
          <>
            <Button onClick={handleClose}>Hủy</Button>
            <Button
              type="primary"
              onClick={handleCreate}
              loading={createMutation.isPending}
            >
              Tạo key
            </Button>
          </>
        )
      }
      destroyOnHidden
    >
      {!generatedKey ? (
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            name="name"
            label="Tên API Key"
            rules={[{ required: true, message: 'Vui lòng nhập tên' }]}
          >
            <Input placeholder="Integration Service Key" />
          </Form.Item>

          <Form.Item
            name="scopes"
            label="Phạm vi quyền (Scopes)"
            rules={[{ required: true, message: 'Chọn ít nhất một scope' }]}
          >
            <Select
              mode="multiple"
              placeholder="Chọn scopes..."
              options={scopeOptions}
            />
          </Form.Item>

          <Form.Item name="expiryDate" label="Ngày hết hạn (để trống = không hết hạn)">
            <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" />
          </Form.Item>
        </Form>
      ) : (
        <div style={{ marginTop: 16 }}>
          <Alert
            type="warning"
            showIcon
            message="Lưu API Key này ngay bây giờ"
            description="Key chỉ được hiển thị một lần duy nhất. Sau khi đóng dialog, bạn không thể xem lại."
            style={{ marginBottom: 16 }}
          />
          <div
            style={{
              background: '#f5f5f5',
              borderRadius: 6,
              padding: '12px 16px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 8,
            }}
          >
            <Text code copyable={false} style={{ wordBreak: 'break-all', fontSize: 13 }}>
              {generatedKey}
            </Text>
            <Button
              icon={copied ? <CheckOutlined /> : <CopyOutlined />}
              onClick={handleCopy}
              type={copied ? 'default' : 'primary'}
              size="small"
            >
              {copied ? 'Đã sao chép' : 'Sao chép'}
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
}
