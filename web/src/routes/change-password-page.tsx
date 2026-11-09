import { useState } from 'react';
import { createRoute, useSearch, useNavigate } from '@tanstack/react-router';
import { Alert, Button, Card, Form, Input, Space, Typography } from 'antd';
import { LockOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { rootRoute } from './root-layout';
import { apiClient } from '@/core/api';
import { GOV_COLORS } from '@/app/theme';
import { appMessage } from '@/core/api/notification-bridge';

// Route: /change-password — interstitial force-change page for MustChangePassword flag.
// Placed at rootRoute level (sibling of /login) — NOT under authenticatedRoute/RouteGuard.
// A user with MustChangePassword=true reaches this page after any 403+X-Must-Change-Password.
export const changePasswordRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/change-password',
  component: ChangePasswordPage,
  validateSearch: (search: Record<string, unknown>) => ({
    reason: typeof search['reason'] === 'string' ? search['reason'] : undefined,
  }),
});

const { Title, Text } = Typography;

interface ChangePasswordFormValues {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

function ChangePasswordPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // TanStack Router typed search — validateSearch ensures shape at route level
  const { reason } = useSearch({ from: '/change-password' });
  const isMustChange = reason === 'must-change';

  const [form] = Form.useForm<ChangePasswordFormValues>();
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(values: ChangePasswordFormValues) {
    setSubmitting(true);
    try {
      await apiClient.post('/account/change-password', {
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
      });
      appMessage.success(t('auth.changePasswordSuccess', { defaultValue: 'Đổi mật khẩu thành công' }));
      form.resetFields();
      // Navigate to root — user is now unblocked
      void navigate({ to: '/' });
    } catch (err: unknown) {
      // Map BE ModelState errors (400/422) back to form fields
      const anyErr = err as { errors?: Record<string, string[]>; message?: string } | null;
      if (anyErr && typeof anyErr === 'object' && anyErr.errors) {
        // BE may return PascalCase field names — normalise to camelCase for AntD.
        // Cast to `any[]` because the generic constraint on setFields requires exact
        // field name literals, but BE errors are runtime strings. This is intentional.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const fieldErrors: any[] = Object.entries(anyErr.errors).map(([field, msgs]) => ({
          name: field.charAt(0).toLowerCase() + field.slice(1),
          errors: msgs as string[],
        }));
        form.setFields(fieldErrors);
      } else {
        appMessage.error(
          anyErr?.message ?? t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }),
        );
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: GOV_COLORS.bgLayout,
      }}
    >
      <Card style={{ width: 420, borderRadius: 12, boxShadow: '0 2px 8px rgba(0,0,0,0.08)' }}>
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <div style={{ textAlign: 'center' }}>
            <LockOutlined style={{ fontSize: 32, color: GOV_COLORS.navy, marginBottom: 8 }} />
            <Title level={4} style={{ color: GOV_COLORS.navy, marginBottom: 4 }}>
              {t('auth.changePasswordTitle')}
            </Title>
          </div>

          {/* Banner shown only when redirected from MustChangePassword gate */}
          {isMustChange && (
            <Alert
              type="info"
              showIcon
              message={t('auth.mustChangePassword')}
            />
          )}

          <Form
            form={form}
            layout="vertical"
            onFinish={handleSubmit}
            style={{ marginTop: 4 }}
          >
            <Form.Item
              name="currentPassword"
              label={t('auth.currentPassword')}
              rules={[{ required: true, message: t('common.required', { defaultValue: 'Bắt buộc' }) }]}
            >
              <Input.Password autoComplete="current-password" />
            </Form.Item>

            <Form.Item
              name="newPassword"
              label={t('auth.newPassword')}
              rules={[
                { required: true, message: t('common.required', { defaultValue: 'Bắt buộc' }) },
                {
                  min: 12,
                  message: t('auth.newPasswordMinLength', {
                    defaultValue: 'Mật khẩu mới phải ít nhất 12 ký tự',
                  }),
                },
              ]}
            >
              <Input.Password autoComplete="new-password" />
            </Form.Item>

            <Form.Item
              name="confirmPassword"
              label={t('auth.confirmPassword')}
              dependencies={['newPassword']}
              rules={[
                { required: true, message: t('common.required', { defaultValue: 'Bắt buộc' }) },
                ({ getFieldValue }) => ({
                  validator(_, value) {
                    if (!value || getFieldValue('newPassword') === value) {
                      return Promise.resolve();
                    }
                    return Promise.reject(
                      new Error(
                        t('auth.passwordMismatch', {
                          defaultValue: 'Mật khẩu xác nhận không khớp',
                        }),
                      ),
                    );
                  },
                }),
              ]}
            >
              <Input.Password autoComplete="new-password" />
            </Form.Item>

            <Form.Item style={{ marginBottom: 0 }}>
              <Button
                type="primary"
                htmlType="submit"
                block
                size="large"
                loading={submitting}
              >
                {t('auth.changePasswordSubmit')}
              </Button>
            </Form.Item>
          </Form>

          <Text type="secondary" style={{ fontSize: 12, textAlign: 'center', display: 'block' }}>
            © {new Date().getFullYear()} AEQUITAS — Vietnamese Gov Framework
          </Text>
        </Space>
      </Card>
    </div>
  );
}
