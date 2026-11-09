// Workflow definition detail page — 8-tab layout for managing a single definition (T5-10 adds migrations tab)

import { useState } from 'react';
import { Spin, Tabs, Typography, Space, Tag, Button, Popconfirm, Modal, Alert, List } from 'antd';
import { appMessage } from '@/core/api/notification-bridge';
import {
  ArrowLeftOutlined,
  CopyOutlined,
  CheckCircleOutlined,
  StopOutlined,
  ExclamationCircleOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import {
  useWorkflowDefinition,
  useCloneWorkflowDefinition,
  useActivateDefinition,
  useDeactivateDefinition,
} from './workflow-api';
import { useBpReferencesForWorkflowDefinition } from '@/features/business-process/business-process-admin-api';
import { DefinitionGeneralTab } from './components/definition-general-tab';
import { DefinitionStatesTab } from './components/definition-states-tab';
import { DefinitionTransitionsTab } from './components/definition-transitions-tab';
import { DefinitionVisualDesignerTab } from './components/definition-visual-designer-tab';
import { DefinitionNotificationsTab } from './components/definition-notifications-tab';
import { DefinitionAssignmentRulesTab } from './components/definition-assignment-rules-tab';
import { DefinitionWebhookConfigTab } from './components/definition-webhook-config-tab';
import { WorkflowVersionHistoryPanel } from './migration/workflow-version-history-panel';
import { WorkflowMigrationPage } from './migration/workflow-migration-page';

const { Title } = Typography;

interface Props {
  definitionId: string;
}

export function WorkflowDefinitionDetailPage({ definitionId }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [bpRefModalOpen, setBpRefModalOpen] = useState(false);
  // checkDeactivateRefs: enabled when Popconfirm opens on an Active definition — pre-fetches refs before user confirms
  const [checkDeactivateRefs, setCheckDeactivateRefs] = useState(false);
  const { data: definition, isLoading } = useWorkflowDefinition(definitionId);
  const cloneMutation = useCloneWorkflowDefinition(definitionId);
  const activateMutation = useActivateDefinition(definitionId);
  const deactivateMutation = useDeactivateDefinition(definitionId);

  // Pre-flight refs — only fetched when deactivate Popconfirm opens (staleTime=0 via hook)
  const { data: bpRefs = [], isFetching: isFetchingRefs } = useBpReferencesForWorkflowDefinition(
    checkDeactivateRefs ? definitionId : null,
  );

  if (isLoading) return <Spin size="large" style={{ display: 'block', margin: '80px auto' }} />;
  if (!definition) return <div>{t('common.notFound')}</div>;

  const handleClone = async () => {
    const cloned = await cloneMutation.mutateAsync();
    appMessage.success(t('workflow.cloneSuccess'));
    navigate({ to: '/admin/workflow/$definitionId', params: { definitionId: cloned.id } });
  };

  const handleToggleActive = async () => {
    if (definition.isActive) {
      // Guard: block is enforced by Popconfirm.okButtonProps.disabled + modal below.
      // By the time user clicks OK, bpRefs is already loaded (fetched on onOpenChange).
      if (bpRefs.length > 0) {
        setBpRefModalOpen(true);
        return;
      }
      await deactivateMutation.mutateAsync();
      appMessage.success(t('workflow.deactivated'));
    } else {
      await activateMutation.mutateAsync();
      appMessage.success(t('workflow.activated'));
    }
    setCheckDeactivateRefs(false);
  };

  const tabItems = [
    { key: 'general', label: t('workflow.tabs.general'), children: <DefinitionGeneralTab definition={definition} /> },
    { key: 'designer', label: t('workflow.tabs.designer'), children: <DefinitionVisualDesignerTab definition={definition} /> },
    { key: 'states', label: `${t('workflow.tabs.states')} (${definition.states.length})`, children: <DefinitionStatesTab definition={definition} /> },
    { key: 'transitions', label: `${t('workflow.tabs.transitions')} (${definition.transitions.length})`, children: <DefinitionTransitionsTab definition={definition} /> },
    { key: 'notifications', label: t('workflow.tabs.notifications'), children: <DefinitionNotificationsTab definitionId={definitionId} /> },
    { key: 'assignments', label: t('workflow.tabs.assignments'), children: <DefinitionAssignmentRulesTab definitionId={definitionId} /> },
    { key: 'webhooks', label: t('workflow.tabs.webhooks'), children: <DefinitionWebhookConfigTab /> },
    {
      key: 'migrations',
      label: t('workflow.tabs.migrations'),
      children: (
        <div>
          <WorkflowVersionHistoryPanel
            definitionKey={definition.definitionKey}
            onMigrateFrom={() => {}}
          />
          <WorkflowMigrationPage
            definitionId={definitionId}
            definitionKey={definition.definitionKey}
          />
        </div>
      ),
    },
  ];

  return (
    <div>
      <Space style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between' }}>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate({ to: '/admin/workflow' })} />
          <Title level={4} style={{ margin: 0 }}>{definition.name}</Title>
          <Tag color={definition.isActive ? 'green' : 'default'}>
            {definition.isActive ? t('workflow.active') : t('workflow.inactive')}
          </Tag>
          <Tag>v{definition.version}</Tag>
        </Space>
        <Space>
          <Popconfirm title={t('workflow.cloneConfirm')} onConfirm={handleClone}>
            <Button icon={<CopyOutlined />} loading={cloneMutation.isPending}>
              {t('workflow.clone')}
            </Button>
          </Popconfirm>
          <Popconfirm
            title={
              definition.isActive && bpRefs.length > 0
                ? t('businessProcessAdmin.refs.cannotDelete', 'Workflow đang được tham chiếu bởi quy trình')
                : definition.isActive
                  ? t('workflow.deactivateConfirm')
                  : t('workflow.activateConfirm')
            }
            onOpenChange={(open) => {
              // Enable refs pre-fetch when deactivate Popconfirm opens; disable on close
              if (definition.isActive) setCheckDeactivateRefs(open);
            }}
            onConfirm={handleToggleActive}
            okButtonProps={{
              // Block OK when fetching or when refs exist — prevents race condition bypass
              disabled: definition.isActive && (isFetchingRefs || bpRefs.length > 0),
              loading: definition.isActive && isFetchingRefs,
            }}
            okText={
              definition.isActive && bpRefs.length > 0
                ? t('businessProcessAdmin.refs.viewRefs', 'Xem chi tiết')
                : t('common.confirm', 'Xác nhận')
            }
            cancelText={t('common.cancel')}
          >
            <Button
              icon={definition.isActive ? <StopOutlined /> : <CheckCircleOutlined />}
              loading={activateMutation.isPending || deactivateMutation.isPending}
              type={definition.isActive ? 'default' : 'primary'}
            >
              {definition.isActive ? t('workflow.deactivate') : t('workflow.activate')}
            </Button>
          </Popconfirm>
        </Space>
      </Space>

      <Tabs items={tabItems} defaultActiveKey="general" destroyOnHidden={false} />

      {/* BP reference blocking modal — shown when BPs reference this WfDef and user tries to deactivate */}
      <Modal
        open={bpRefModalOpen}
        title={
          <Space>
            <ExclamationCircleOutlined style={{ color: '#faad14' }} />
            {t('businessProcessAdmin.refs.title', 'Không thể vô hiệu hóa workflow')}
          </Space>
        }
        onCancel={() => { setBpRefModalOpen(false); setCheckDeactivateRefs(false); }}
        footer={
          <Button onClick={() => { setBpRefModalOpen(false); setCheckDeactivateRefs(false); }}>
            {t('common.cancel', 'Đóng')}
          </Button>
        }
        destroyOnHidden
      >
        <Alert
          type="warning"
          message={t('businessProcessAdmin.refs.cannotDelete', 'Workflow này đang được sử dụng bởi các quy trình sau. Hãy xóa các tham chiếu trước khi vô hiệu hóa.')}
          style={{ marginBottom: 12 }}
        />
        <List
          loading={isFetchingRefs}
          size="small"
          dataSource={bpRefs}
          renderItem={(ref) => (
            <List.Item>
              <Space>
                <Typography.Text code>{ref.code}</Typography.Text>
                <Typography.Text>{ref.name}</Typography.Text>
                {!ref.isActive && <Tag color="default">Inactive</Tag>}
              </Space>
            </List.Item>
          )}
        />
      </Modal>
    </div>
  );
}
