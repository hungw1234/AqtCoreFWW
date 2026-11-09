// business-process-detail-page.tsx — read-only detail + inline metadata edit + activate/deactivate
import { useState, createElement } from 'react';
import {
  Button,
  Space,
  Tag,
  Descriptions,
  Form,
  Input,
  Spin,
  Alert,
  Popconfirm,
  Typography,
  Divider,
} from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { usePermissions } from '@/features/auth/use-permissions';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import type { NormalizedApiError } from '@/core/api/api-error';
import { formatGraphViolations } from '@/core/api/api-error';
import { BusinessProcessFormMapsView } from './business-process-form-maps-view';
import {
  useBpById,
  useUpdateBp,
  useActivateBp,
  useDeactivateBp,
  getGetApiV1BusinessProcessesQueryKey,
  getGetApiV1BusinessProcessesIdQueryKey,
} from './business-process-admin-api';
import { appMessage } from '@/core/api/notification-bridge';

const { Title } = Typography;

interface Props {
  id: string;
}

interface MetadataForm {
  name: string;
  description?: string | null;
}

export function BusinessProcessDetailPage({ id }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { hasPermission, isPermissionsLoading } = usePermissions();

  const canView = hasPermission('BUSINESSPROCESS.BP.VIEW');
  const canManage = hasPermission('BUSINESSPROCESS.BP.MANAGE');
  const canActivate = hasPermission('BUSINESSPROCESS.BP.ACTIVATE');

  const { data: bp, isLoading, isError } = useBpById(id);
  const updateMutation = useUpdateBp();
  const activateMutation = useActivateBp();
  const deactivateMutation = useDeactivateBp();

  const [editMode, setEditMode] = useState(false);
  const [isEditingFormMaps, setIsEditingFormMaps] = useState(false);
  const [metaForm] = Form.useForm<MetadataForm>();

  function invalidateAll() {
    void queryClient.invalidateQueries({ queryKey: getGetApiV1BusinessProcessesQueryKey() });
    void queryClient.invalidateQueries({ queryKey: getGetApiV1BusinessProcessesIdQueryKey(id) });
  }

  function handleEditStart() {
    if (!bp) return;
    metaForm.setFieldsValue({ name: bp.name, description: bp.description ?? '' });
    setEditMode(true);
  }

  function handleEditCancel() {
    setEditMode(false);
    metaForm.resetFields();
  }

  async function handleSave() {
    try {
      await metaForm.validateFields();
    } catch {
      return;
    }
    const vals = metaForm.getFieldsValue();
    updateMutation.mutate(
      { id, data: { name: vals.name, description: vals.description ?? null } },
      {
        onSuccess: () => {
          appMessage.success(t('common.operationSuccess', { defaultValue: 'Thao tác thành công' }));
          setEditMode(false);
          invalidateAll();
        },
        onError: (err: unknown) => {
          const apiErr = err as NormalizedApiError | null;
          appMessage.error(apiErr?.message ?? t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }));
        },
      },
    );
  }

  function handleActivate() {
    activateMutation.mutate(
      { id },
      {
        onSuccess: () => { appMessage.success(t('page.businessProcessAdmin.activated', { defaultValue: 'Đã kích hoạt.' })); invalidateAll(); },
        onError: (err: unknown) => {
          const apiErr = err as NormalizedApiError | null;
          // When the activation fails due to graph violations, render them as a multi-line
          // bullet list (same UX as workflow-Save). AntD message collapses "\n" so we wrap
          // in a pre-line node. 8s gives the user time to read the full violation list.
          if (apiErr?.violations && apiErr.violations.length > 0) {
            appMessage.error(
              createElement('div', { style: { whiteSpace: 'pre-line' } }, formatGraphViolations(apiErr.violations, t)),
              8,
            );
          } else {
            appMessage.error(apiErr?.message ?? t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }));
          }
        },
      },
    );
  }

  function handleDeactivate() {
    deactivateMutation.mutate(
      { id },
      {
        onSuccess: () => { appMessage.success(t('page.businessProcessAdmin.deactivated', { defaultValue: 'Đã vô hiệu hóa.' })); invalidateAll(); },
        onError: (err: unknown) => {
          const apiErr = err as NormalizedApiError | null;
          appMessage.error(apiErr?.message ?? t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }));
        },
      },
    );
  }

  if (isPermissionsLoading) return <Spin size="large" style={{ display: 'block', margin: '80px auto' }} />;

  if (!canView) {
    return (
      <Alert
        type="error"
        message={t('common.forbidden', { defaultValue: 'Bạn không có quyền truy cập trang này.' })}
        style={{ margin: 24 }}
      />
    );
  }

  if (isLoading) return <Spin size="large" style={{ display: 'block', margin: '80px auto' }} />;
  if (isError || !bp) {
    return (
      <Alert
        type="error"
        message={t('page.businessProcessAdmin.notFound', { defaultValue: 'Không tìm thấy quy trình.' })}
        style={{ margin: 24 }}
      />
    );
  }

  return (
    <>
      {/* Bilateral header — mirrors workflow-definition-detail-page convention: back+title+tag
          on the left, contextual actions on the right. AdminPageHeader is list-page-oriented
          and doesn't carry status tags or back navigation, so detail pages use this manual row. */}
      <Space style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between', width: '100%' }} wrap>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => void navigate({ to: '/admin/business-processes' })} />
          <Title level={4} style={{ margin: 0 }}>{bp.name}</Title>
          <Tag color={bp.isActive ? 'green' : 'default'}>
            {bp.isActive
              ? t('common.active', { defaultValue: 'Hoạt động' })
              : t('common.inactive', { defaultValue: 'Tắt' })}
          </Tag>
        </Space>
        <Space>
          {/* Bridge admin→runtime: an activated BP's only run entry is the orphaned-from-
              admin /business-process-runs/new wizard. Surface it here so activating a BP
              leads directly to using it. Gated by BP.VIEW — same perm as the runtime menu,
              so the CTA only shows to users who can actually start a run. */}
          {canView && bp.isActive && !editMode && (
            <Button
              type="primary"
              onClick={() => void navigate({ to: '/business-process-runs/new' })}
            >
              {t('page.businessProcessAdmin.startRun', { defaultValue: 'Bắt đầu chạy' })}
            </Button>
          )}

          {canManage && !editMode && (
            <Button onClick={handleEditStart}>
              {t('common.edit', { defaultValue: 'Sửa' })}
            </Button>
          )}

          {canActivate && !bp.isActive && (
            <Popconfirm
              title={t('page.businessProcessAdmin.confirmActivate', { defaultValue: 'Kích hoạt quy trình này?' })}
              onConfirm={handleActivate}
              okText={t('common.confirm', { defaultValue: 'Xác nhận' })}
              cancelText={t('common.cancel', { defaultValue: 'Huỷ' })}
            >
              <Button type="primary" loading={activateMutation.isPending}>
                {t('page.businessProcessAdmin.activate', { defaultValue: 'Kích hoạt' })}
              </Button>
            </Popconfirm>
          )}

          {canActivate && bp.isActive && (
            <Popconfirm
              title={t('page.businessProcessAdmin.confirmDeactivate', { defaultValue: 'Vô hiệu hóa quy trình này?' })}
              onConfirm={handleDeactivate}
              okText={t('common.confirm', { defaultValue: 'Xác nhận' })}
              cancelText={t('common.cancel', { defaultValue: 'Huỷ' })}
            >
              <Button danger loading={deactivateMutation.isPending}>
                {t('page.businessProcessAdmin.deactivate', { defaultValue: 'Vô hiệu hóa' })}
              </Button>
            </Popconfirm>
          )}
        </Space>
      </Space>

      <AdminContentCard>
      {!editMode ? (
        <Descriptions bordered column={1} size="small" style={{ marginBottom: 24 }}>
          <Descriptions.Item label={t('page.businessProcessAdmin.fieldCode', { defaultValue: 'Mã' })}>{bp.code}</Descriptions.Item>
          <Descriptions.Item label={t('page.businessProcessAdmin.fieldName', { defaultValue: 'Tên' })}>{bp.name}</Descriptions.Item>
          <Descriptions.Item label={t('page.businessProcessAdmin.fieldDescription', { defaultValue: 'Mô tả' })}>{bp.description ?? '—'}</Descriptions.Item>
          <Descriptions.Item label={t('page.businessProcessAdmin.fieldWorkflow', { defaultValue: 'Workflow ID' })}>{bp.workflowDefinitionId}</Descriptions.Item>
          <Descriptions.Item label={t('page.businessProcessAdmin.fieldIntakeForm', { defaultValue: 'Intake Form ID' })}>{bp.intakeFormTemplateId ?? '—'}</Descriptions.Item>
          <Descriptions.Item label={t('page.businessProcessAdmin.fieldIntakeVersion', { defaultValue: 'Intake Version' })}>{bp.intakeFormTemplateVersion ?? '—'}</Descriptions.Item>
          <Descriptions.Item label={t('page.businessProcessAdmin.fieldCreatedAt', { defaultValue: 'Tạo lúc' })}>{bp.createdAt}</Descriptions.Item>
        </Descriptions>
      ) : (
        <Form form={metaForm} layout="vertical" style={{ maxWidth: 560, marginBottom: 24 }}>
          <Form.Item
            name="name"
            label={t('page.businessProcessAdmin.fieldName', { defaultValue: 'Tên quy trình' })}
            rules={[
              { required: true, message: t('page.businessProcessAdmin.nameRequired', { defaultValue: 'Vui lòng nhập tên' }) },
              { max: 200 },
            ]}
          >
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item
            name="description"
            label={t('page.businessProcessAdmin.fieldDescription', { defaultValue: 'Mô tả' })}
            rules={[{ max: 1000 }]}
          >
            <Input.TextArea rows={3} maxLength={1000} showCount />
          </Form.Item>
          <Space>
            <Button type="primary" onClick={() => void handleSave()} loading={updateMutation.isPending}>
              {t('common.save', { defaultValue: 'Lưu' })}
            </Button>
            <Button onClick={handleEditCancel}>{t('common.cancel', { defaultValue: 'Huỷ' })}</Button>
          </Space>
        </Form>
      )}

      <Divider>
        <Space>
          {t('page.businessProcessAdmin.formMapsTitle', { defaultValue: 'Cấu hình biểu mẫu' })}
          {canManage && !isEditingFormMaps && (
            <Button size="small" onClick={() => setIsEditingFormMaps(true)}>
              {t('page.businessProcessAdmin.formMaps.edit.editButton', { defaultValue: 'Edit Form Maps' })}
            </Button>
          )}
          {isEditingFormMaps && (
            <Button size="small" onClick={() => setIsEditingFormMaps(false)}>
              {t('page.businessProcessAdmin.formMaps.edit.cancelButton', { defaultValue: 'Cancel' })}
            </Button>
          )}
        </Space>
      </Divider>

      {isEditingFormMaps ? (
        <BusinessProcessFormMapsView
          bp={bp}
          editMode={true}
          onSaved={() => setIsEditingFormMaps(false)}
        />
      ) : (
        <BusinessProcessFormMapsView
          stateForms={bp.stateForms}
          transitionForms={bp.transitionForms}
        />
      )}
      </AdminContentCard>
    </>
  );
}
