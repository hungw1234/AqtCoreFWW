import { useState } from 'react';
import { Button, Space, Typography, Form, Input, Card, Tabs } from 'antd';
import { LockOutlined, IdcardOutlined, SafetyOutlined } from '@ant-design/icons';
import { useAuth } from '@/features/auth';
import { useTranslation } from 'react-i18next';
import { useChangePassword } from './profile-api';
import { appMessage } from '@/core/api/notification-bridge';
import { SignatureManagementTab } from './signatures/signature-management-tab';
import { ProfileHero } from './components/profile-hero';
import { SectionHeader } from './components/section-header';

// Normalises the OIDC `role` claim (string | string[] | undefined) into a string list so both the
// hero and the account card render it as tags consistently.
function toRoleList(role: unknown): string[] {
  if (Array.isArray(role)) return role.map(String).filter(Boolean);
  if (typeof role === 'string' && role.trim()) return [role];
  return [];
}

// ChangePasswordCard — self-service password change. Kept as its own titled section (was a lone
// ghost button) so security lives in a clear, always-visible card rather than a hidden toggle.
function ChangePasswordCard() {
  const { t } = useTranslation();
  const [showForm, setShowForm] = useState(false);
  const [form] = Form.useForm();
  const changePassword = useChangePassword();

  async function handleSubmit(values: { currentPassword: string; newPassword: string }) {
    try {
      await changePassword.mutateAsync(values);
      appMessage.success(t('profile.passwordChanged'));
      form.resetFields();
      setShowForm(false);
    } catch {
      appMessage.error(t('profile.passwordError'));
    }
  }

  return (
    <Card variant="borderless" style={{ boxShadow: 'var(--elevation-1)' }}>
      <SectionHeader
        title={t('profile.securityTitle')}
        description={t('profile.securityHint')}
        extra={
          !showForm ? (
            <Button icon={<LockOutlined />} onClick={() => setShowForm(true)}>
              {t('profile.changePassword')}
            </Button>
          ) : undefined
        }
      />

      {showForm && (
        // Constrain the form measure for readability — full-card-width password inputs read poorly.
        <Form form={form} layout="vertical" onFinish={handleSubmit} style={{ maxWidth: 420, marginTop: 20 }}>
          <Form.Item
            name="currentPassword"
            label={t('profile.currentPassword')}
            rules={[{ required: true, message: t('profile.currentPasswordRequired') }]}
          >
            <Input.Password autoComplete="current-password" />
          </Form.Item>
          <Form.Item
            name="newPassword"
            label={t('profile.newPassword')}
            rules={[
              { required: true, message: t('profile.newPasswordRequired') },
              { min: 8, message: t('profile.newPasswordMin') },
            ]}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Form.Item
            name="confirmPassword"
            label={t('profile.confirmPassword')}
            dependencies={['newPassword']}
            rules={[
              { required: true, message: t('profile.confirmPasswordRequired') },
              ({ getFieldValue }) => ({
                validator(_, value) {
                  if (!value || getFieldValue('newPassword') === value) return Promise.resolve();
                  return Promise.reject(new Error(t('profile.passwordMismatch')));
                },
              }),
            ]}
          >
            <Input.Password autoComplete="new-password" />
          </Form.Item>
          <Form.Item style={{ marginBottom: 0 }}>
            <Space>
              <Button type="primary" htmlType="submit" loading={changePassword.isPending}>
                {t('profile.save')}
              </Button>
              <Button onClick={() => { setShowForm(false); form.resetFields(); }}>
                {t('common.cancel')}
              </Button>
            </Space>
          </Form.Item>
        </Form>
      )}
    </Card>
  );
}

// ProfileInfoTab — the account's security actions (change password). Identity (name/email/roles) is
// shown ONCE in the page-level hero above the tabs, so it is never duplicated on the same screen.
function ProfileInfoTab() {
  return <ChangePasswordCard />;
}

// ProfilePage — page-level identity hero + profile info / digital-signature management tabs.
// The hero renders once above the tabs so identity persists across both. The signature tab sources
// its data from /me/signature-overview, NOT OIDC claims.
export function ProfilePage() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const profile = user?.profile;
  const roles = toRoleList(profile?.role);

  // Root follows the authenticated-page convention (see admin-dashboard): a full-width flex column
  // that relies on <main>'s own 24px padding. No extra margin / maxWidth / horizontal padding here —
  // those double the outer gutter and leave large empty side-margins on wide screens.
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>{t('nav.profile')}</Typography.Title>

      <ProfileHero name={profile?.name} email={profile?.email} roles={roles} />

      <Tabs
        defaultActiveKey="info"
        items={[
          {
            key: 'info',
            label: <span><IdcardOutlined /> {t('profile.tab.info')}</span>,
            children: <ProfileInfoTab />,
          },
          {
            key: 'signatures',
            label: <span><SafetyOutlined /> {t('profile.tab.signatures')}</span>,
            children: <SignatureManagementTab />,
          },
        ]}
      />
    </div>
  );
}
