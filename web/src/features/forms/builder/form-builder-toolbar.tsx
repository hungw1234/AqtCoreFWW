// form-builder-toolbar.tsx — sticky top bar: template metadata + publish/preview actions

import { Button, Space, Tag, Typography, theme } from 'antd';
import { ArrowLeftOutlined, EyeOutlined } from '@ant-design/icons';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { FormTemplateDto, FormStatus } from '../form-types';

const { Title } = Typography;

const STATUS_COLOR: Record<FormStatus, string> = {
  Draft: 'default',
  Materializing: 'blue',
  Active: 'green',
  Inactive: 'orange',
};

interface FormBuilderToolbarProps {
  template: FormTemplateDto;
  onPublish: () => void;
  onPreview: () => void;
  isPublishing: boolean;
}

export function FormBuilderToolbar({
  template,
  onPublish,
  onPreview,
  isPublishing,
}: FormBuilderToolbarProps) {
  const { t } = useTranslation();
  const { token } = theme.useToken();
  const navigate = useNavigate();

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '8px 16px',
        borderBottom: `1px solid ${token.colorBorderSecondary}`,
        background: token.colorBgContainer,
        flexShrink: 0,
        gap: 8,
      }}
    >
      {/* Left: back + template name */}
      <Space size={8}>
        <Button
          type="text"
          icon={<ArrowLeftOutlined />}
          onClick={() => navigate({ to: '/forms' })}
          size="small"
        />
        <Title level={5} style={{ margin: 0 }}>
          {template.name}
        </Title>
        <Tag color={STATUS_COLOR[template.status]}>
          {t(`page.forms.status.${template.status}`, { defaultValue: template.status })}
        </Tag>
      </Space>

      {/* Right: actions */}
      <Space size={8}>
        <Button size="small" icon={<EyeOutlined />} onClick={onPreview}>
          {t('forms.builder.toolbar.preview')}
        </Button>
        {template.status === 'Draft' && (
          <Button
            size="small"
            type="primary"
            loading={isPublishing}
            onClick={onPublish}
          >
            {t('page.forms.detail.publishBtn')}
          </Button>
        )}
      </Space>
    </div>
  );
}
