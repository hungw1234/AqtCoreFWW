// business-process-form-maps-view.tsx — read-only + editable state/transition form map configuration
// P7-FU1: dual-mode — read-only (editMode=false) or inline editor (editMode=true)
// P7-FU1.2: state + transition pickers swapped from raw InputNumber → Select with display names
//           sourced from useGetApiV1WorkflowDefinitionsId (BP detail surfaces workflowDefinitionPublicId).
import { useMemo, useState } from 'react';
import {
  Tabs,
  Table,
  Tag,
  Button,
  Switch,
  InputNumber,
  Select,
  Space,
  Card,
} from 'antd';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useQueryClient } from '@tanstack/react-query';
import type { BpStateFormMapDto, BpTransitionFormMapDto, BpDefinitionDto } from './business-process-run-api';
import {
  useReplaceFormMaps,
  useFormTemplatePicker,
  getGetApiV1BusinessProcessesIdQueryKey,
} from './business-process-admin-api';
import { useGetApiV1WorkflowDefinitionsId } from '@/api/generated/workflow-definitions/workflow-definitions';
import type { WorkflowDefinitionDto } from '@/features/workflow/workflow-types';
import type { NormalizedApiError } from '@/core/api/api-error';
import { BpWorkflowFlowReadOnly } from './bp-workflow-flow-readonly';
import { appMessage } from '@/core/api/notification-bridge';

// ─── Read-only view props (legacy / external call site) ──────────────────────

interface ReadOnlyProps {
  stateForms: BpStateFormMapDto[];
  transitionForms: BpTransitionFormMapDto[];
  editMode?: false;
  bp?: undefined;
  onSaved?: undefined;
}

// ─── Edit-mode props ──────────────────────────────────────────────────────────

interface EditModeProps {
  bp: BpDefinitionDto;
  editMode: boolean;
  onSaved?: () => void;
  stateForms?: undefined;
  transitionForms?: undefined;
}

type Props = ReadOnlyProps | EditModeProps;

// ─── Local row types for edit mode ────────────────────────────────────────────

interface StateFormRow {
  _key: string;
  workflowStateId: number | undefined;
  formTemplatePublicId: string | undefined;
  version: number;
  isRequired: boolean;
}

interface TransitionFormRow {
  _key: string;
  workflowTransitionId: number | undefined;
  formTemplatePublicId: string | undefined;
  version: number;
  isRequired: boolean;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function makeStateRow(source?: BpStateFormMapDto): StateFormRow {
  return {
    _key: source ? `existing-${String(source.id)}` : `new-${crypto.randomUUID()}`,
    workflowStateId: source ? Number(source.workflowStateId) : undefined,
    // BUG2 fix 2026-05-18: BE now resolves formTemplatePublicId server-side. Bind directly —
    // new (unsaved) rows start blank; user picks from the select.
    formTemplatePublicId: source?.formTemplatePublicId ?? undefined,
    version: source?.formTemplateVersion ?? 1,
    isRequired: source?.isRequired ?? false,
  };
}

function makeTransitionRow(source?: BpTransitionFormMapDto): TransitionFormRow {
  return {
    _key: source ? `existing-${String(source.id)}` : `new-${crypto.randomUUID()}`,
    workflowTransitionId: source ? Number(source.workflowTransitionId) : undefined,
    // BUG2 fix 2026-05-18: bound from server-resolved publicId (see makeStateRow).
    formTemplatePublicId: source?.formTemplatePublicId ?? undefined,
    version: source?.formTemplateVersion ?? 1,
    isRequired: source?.isRequired ?? false,
  };
}

// ─── Component ────────────────────────────────────────────────────────────────

export function BusinessProcessFormMapsView(props: Props) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();

  // Resolve stateForms/transitionForms from either prop shape
  const stateForms: BpStateFormMapDto[] =
    props.editMode !== true
      ? (props.stateForms ?? [])
      : (props.bp?.stateForms ?? []);
  const transitionForms: BpTransitionFormMapDto[] =
    props.editMode !== true
      ? (props.transitionForms ?? [])
      : (props.bp?.transitionForms ?? []);

  // ─── Edit-mode local state ─────────────────────────────────────────────────
  const [stateRows, setStateRows] = useState<StateFormRow[]>(() =>
    stateForms.map(makeStateRow),
  );
  const [transitionRows, setTransitionRows] = useState<TransitionFormRow[]>(() =>
    transitionForms.map(makeTransitionRow),
  );

  const { data: formTemplateData } = useFormTemplatePicker();
  const formTemplateOptions = (formTemplateData?.items ?? []).map((f) => ({
    label: `${f.name} (v${f.version})`,
    value: f.id,
    version: f.version,
  }));

  // P7-FU1.2: fetch workflow definition for state/transition picker display names.
  // Only fetched in edit mode when bp surfaces workflowDefinitionPublicId.
  // i18n: prefers Vi labels; falls back to machine name when label missing.
  const workflowDefinitionPublicId =
    props.editMode === true ? (props.bp?.workflowDefinitionPublicId ?? '') : '';
  const { data: workflowDefinition } = useGetApiV1WorkflowDefinitionsId<WorkflowDefinitionDto>(
    workflowDefinitionPublicId,
    { query: { enabled: !!workflowDefinitionPublicId } },
  );

  const stateOptions = useMemo(() => {
    const states = workflowDefinition?.states ?? [];
    return states.map((s) => ({
      value: Number(s.id),
      label: `${s.displayNameVi || s.name} (#${s.id})`,
    }));
  }, [workflowDefinition]);

  const stateNameLookup = useMemo(() => {
    const m = new Map<number, string>();
    for (const s of workflowDefinition?.states ?? []) {
      m.set(Number(s.id), s.displayNameVi || s.name);
    }
    return m;
  }, [workflowDefinition]);

  const transitionOptions = useMemo(() => {
    const transitions = workflowDefinition?.transitions ?? [];
    return transitions.map((tr) => {
      const fromName = stateNameLookup.get(Number(tr.fromStateId)) ?? `#${tr.fromStateId}`;
      const toName = stateNameLookup.get(Number(tr.toStateId)) ?? `#${tr.toStateId}`;
      const label = tr.actionLabelVi || tr.actionName;
      return {
        value: Number(tr.id),
        label: `${label}: ${fromName} → ${toName}`,
      };
    });
  }, [workflowDefinition, stateNameLookup]);

  // P8-FU2.1.1: per-row option-disable filter — transitions already chosen in another row
  // are rendered disabled in this row's picker (BE/FE single-form-per-transition contract
  // already enforced in handleSave). UX hint that prevents the save-time toast.
  const transitionUsageByRowKey = useMemo(() => {
    const owners = new Map<number, string>();
    for (const r of transitionRows) {
      if (r.workflowTransitionId !== undefined && !owners.has(r.workflowTransitionId)) {
        owners.set(r.workflowTransitionId, r._key);
      }
    }
    return owners;
  }, [transitionRows]);

  // BUG2 fix 2026-05-18: the former backfill effect resolved formTemplatePublicId client-side
  // via `Number(f.id) === bigintId` — f.id is a Guid string so Number() was always NaN and the
  // lookup never matched (FE has no bigint↔Guid bridge). Resolution moved server-side
  // (IFormTemplateQueryClient); rows now bind directly in makeStateRow/makeTransitionRow.

  const replaceFormMapsMutation = useReplaceFormMaps();

  // ─── Read-only mode ────────────────────────────────────────────────────────

  if (!props.editMode) {
    return <ReadOnlyTabs stateForms={stateForms} transitionForms={transitionForms} t={t} />;
  }

  // ─── Edit-mode helpers ─────────────────────────────────────────────────────

  const bp = props.bp as BpDefinitionDto;

  function updateStateRow(key: string, patch: Partial<StateFormRow>) {
    setStateRows((prev) =>
      prev.map((r) => (r._key === key ? { ...r, ...patch } : r)),
    );
  }

  function updateTransitionRow(key: string, patch: Partial<TransitionFormRow>) {
    setTransitionRows((prev) =>
      prev.map((r) => (r._key === key ? { ...r, ...patch } : r)),
    );
  }

  function onFormTemplateSelectState(key: string, publicId: string) {
    const opt = formTemplateOptions.find((o) => o.value === publicId);
    updateStateRow(key, {
      formTemplatePublicId: publicId,
      // Auto-fill version to latest when template chosen
      version: opt?.version ?? 1,
    });
  }

  function onFormTemplateSelectTransition(key: string, publicId: string) {
    const opt = formTemplateOptions.find((o) => o.value === publicId);
    updateTransitionRow(key, {
      formTemplatePublicId: publicId,
      version: opt?.version ?? 1,
    });
  }

  async function handleSave() {
    // Validate: all rows must have both state/transition ID and form template set
    const requiredMsg = t('page.businessProcessAdmin.formMaps.edit.validateRequired', {
      defaultValue: 'Required.',
    });
    const stateValid = stateRows.every((r) => r.workflowStateId !== undefined && r.formTemplatePublicId);
    const transValid = transitionRows.every((r) => r.workflowTransitionId !== undefined && r.formTemplatePublicId);
    if (!stateValid || !transValid) {
      void appMessage.error(requiredMsg);
      return;
    }

    // P8-FU2.1: BE rejects transitions with >1 form binding (ExecuteBusinessProcessTransitionCommand
    // accepts only a single transitionFormData pair). Gate at designer-save time so admins never
    // persist a configuration that would surface a 422 to BP-Run users.
    const dupTransitionId = transitionRows
      .map((r) => r.workflowTransitionId as number)
      .find((id, idx, arr) => arr.indexOf(id) !== idx);
    if (dupTransitionId !== undefined) {
      void appMessage.error(
        t('page.businessProcessAdmin.formMaps.edit.duplicateTransition', {
          defaultValue: 'Mỗi chuyển trạng thái chỉ được cấu hình tối đa một form.',
        }),
      );
      return;
    }

    replaceFormMapsMutation.mutate(
      {
        id: bp.id,
        ifMatchETag: bp.rowVersion,
        data: {
          stateForms: stateRows.map((r) => ({
            workflowStateId: r.workflowStateId as number,
            formTemplatePublicId: r.formTemplatePublicId as string,
            version: r.version,
            isRequired: r.isRequired,
          })),
          transitionForms: transitionRows.map((r) => ({
            workflowTransitionId: r.workflowTransitionId as number,
            formTemplatePublicId: r.formTemplatePublicId as string,
            version: r.version,
            isRequired: r.isRequired,
          })),
        },
      },
      {
        onSuccess: () => {
          void appMessage.success(
            t('page.businessProcessAdmin.formMaps.edit.saveSuccess', {
              defaultValue: 'Form maps saved.',
            }),
          );
          // Invalidate BP detail so fresh rowVersion loads for next edit
          void queryClient.invalidateQueries({
            queryKey: getGetApiV1BusinessProcessesIdQueryKey(bp.id),
          });
          props.onSaved?.();
        },
        onError: (err: unknown) => {
          const apiErr = err as NormalizedApiError | null;
          if (apiErr?.status === 412) {
            void appMessage.error(
              t('page.businessProcessAdmin.formMaps.edit.staleError', {
                defaultValue: 'Form maps changed by another user — please refresh.',
              }),
            );
          } else {
            void appMessage.error(apiErr?.message ?? t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }));
          }
        },
      },
    );
  }

  // ─── Edit-mode state-form columns ─────────────────────────────────────────

  const stateEditColumns: ColumnsType<StateFormRow> = [
    {
      title: t('page.businessProcessAdmin.formMaps.col.state', { defaultValue: 'Trạng thái' }),
      key: 'workflowStateId',
      width: 240,
      render: (_, row) => (
        <Select
          style={{ width: '100%' }}
          showSearch
          optionFilterProp="label"
          placeholder={t('page.businessProcessAdmin.formMaps.edit.selectState', {
            defaultValue: 'Chọn trạng thái...',
          })}
          options={stateOptions}
          value={row.workflowStateId}
          onChange={(v: number) => updateStateRow(row._key, { workflowStateId: v ?? undefined })}
          notFoundContent={
            workflowDefinitionPublicId
              ? t('common.noData', { defaultValue: 'Không có dữ liệu' })
              : t('page.businessProcessAdmin.formMaps.edit.noWorkflowDefinition', {
                  defaultValue: 'Workflow chưa được liên kết',
                })
          }
        />
      ),
    },
    {
      title: t('page.businessProcessAdmin.formMaps.col.formTemplateId', { defaultValue: 'Form Template' }),
      key: 'formTemplatePublicId',
      render: (_, row) => (
        <Select
          style={{ width: '100%' }}
          showSearch
          optionFilterProp="label"
          placeholder="Form template..."
          options={formTemplateOptions}
          value={row.formTemplatePublicId}
          onChange={(v: string) => onFormTemplateSelectState(row._key, v)}
        />
      ),
    },
    {
      title: t('page.businessProcessAdmin.formMaps.col.version', { defaultValue: 'Version' }),
      key: 'version',
      width: 90,
      render: (_, row) => (
        <InputNumber
          min={1}
          style={{ width: '100%' }}
          value={row.version}
          onChange={(v) => updateStateRow(row._key, { version: v ?? 1 })}
        />
      ),
    },
    {
      title: t('page.businessProcessAdmin.formMaps.col.required', { defaultValue: 'Required' }),
      key: 'isRequired',
      width: 90,
      render: (_, row) => (
        <Switch
          checked={row.isRequired}
          onChange={(v) => updateStateRow(row._key, { isRequired: v })}
        />
      ),
    },
    {
      key: 'action',
      width: 48,
      render: (_, row) => (
        <Button
          danger
          size="small"
          icon={<DeleteOutlined />}
          onClick={() => setStateRows((prev) => prev.filter((r) => r._key !== row._key))}
        />
      ),
    },
  ];

  // ─── Edit-mode transition-form columns ────────────────────────────────────

  const transitionEditColumns: ColumnsType<TransitionFormRow> = [
    {
      title: t('page.businessProcessAdmin.formMaps.col.transition', { defaultValue: 'Chuyển trạng thái' }),
      key: 'workflowTransitionId',
      width: 300,
      render: (_, row) => (
        <Select
          style={{ width: '100%' }}
          showSearch
          optionFilterProp="label"
          placeholder={t('page.businessProcessAdmin.formMaps.edit.selectTransition', {
            defaultValue: 'Chọn chuyển trạng thái...',
          })}
          options={transitionOptions.map((o) => {
            const owner = transitionUsageByRowKey.get(o.value);
            return owner !== undefined && owner !== row._key ? { ...o, disabled: true } : o;
          })}
          value={row.workflowTransitionId}
          onChange={(v: number) => updateTransitionRow(row._key, { workflowTransitionId: v ?? undefined })}
          notFoundContent={
            workflowDefinitionPublicId
              ? t('common.noData', { defaultValue: 'Không có dữ liệu' })
              : t('page.businessProcessAdmin.formMaps.edit.noWorkflowDefinition', {
                  defaultValue: 'Workflow chưa được liên kết',
                })
          }
        />
      ),
    },
    {
      title: t('page.businessProcessAdmin.formMaps.col.formTemplateId', { defaultValue: 'Form Template' }),
      key: 'formTemplatePublicId',
      render: (_, row) => (
        <Select
          style={{ width: '100%' }}
          showSearch
          optionFilterProp="label"
          placeholder="Form template..."
          options={formTemplateOptions}
          value={row.formTemplatePublicId}
          onChange={(v: string) => onFormTemplateSelectTransition(row._key, v)}
        />
      ),
    },
    {
      title: t('page.businessProcessAdmin.formMaps.col.version', { defaultValue: 'Version' }),
      key: 'version',
      width: 90,
      render: (_, row) => (
        <InputNumber
          min={1}
          style={{ width: '100%' }}
          value={row.version}
          onChange={(v) => updateTransitionRow(row._key, { version: v ?? 1 })}
        />
      ),
    },
    {
      title: t('page.businessProcessAdmin.formMaps.col.required', { defaultValue: 'Required' }),
      key: 'isRequired',
      width: 90,
      render: (_, row) => (
        <Switch
          checked={row.isRequired}
          onChange={(v) => updateTransitionRow(row._key, { isRequired: v })}
        />
      ),
    },
    {
      key: 'action',
      width: 48,
      render: (_, row) => (
        <Button
          danger
          size="small"
          icon={<DeleteOutlined />}
          onClick={() => setTransitionRows((prev) => prev.filter((r) => r._key !== row._key))}
        />
      ),
    },
  ];

  // ─── Edit-mode render ──────────────────────────────────────────────────────

  return (
    <>
      {/* Form-config block — full width, on top */}
      <div>
          <Tabs
            items={[
              {
                key: 'state-forms',
                label: t('page.businessProcessAdmin.tabStateForms', { defaultValue: 'Form theo trạng thái' }),
                children: (
                  <>
                    <Table<StateFormRow>
                      rowKey="_key"
                      dataSource={stateRows}
                      columns={stateEditColumns}
                      size="small"
                      pagination={false}
                      locale={{ emptyText: t('common.noData', { defaultValue: 'Không có dữ liệu' }) }}
                    />
                    <Button
                      type="dashed"
                      icon={<PlusOutlined />}
                      style={{ marginTop: 8, width: '100%' }}
                      onClick={() => setStateRows((prev) => [...prev, makeStateRow()])}
                    >
                      {t('page.businessProcessAdmin.formMaps.edit.addState', { defaultValue: '+ Add State Form' })}
                    </Button>
                  </>
                ),
              },
              {
                key: 'transition-forms',
                label: t('page.businessProcessAdmin.tabTransitionForms', { defaultValue: 'Form theo chuyển trạng thái' }),
                children: (
                  <>
                    <Table<TransitionFormRow>
                      rowKey="_key"
                      dataSource={transitionRows}
                      columns={transitionEditColumns}
                      size="small"
                      pagination={false}
                      locale={{ emptyText: t('common.noData', { defaultValue: 'Không có dữ liệu' }) }}
                    />
                    <Button
                      type="dashed"
                      icon={<PlusOutlined />}
                      style={{ marginTop: 8, width: '100%' }}
                      onClick={() => setTransitionRows((prev) => [...prev, makeTransitionRow()])}
                    >
                      {t('page.businessProcessAdmin.formMaps.edit.addTransition', {
                        defaultValue: '+ Add Transition Form',
                      })}
                    </Button>
                  </>
                ),
              },
            ]}
          />
          <Space style={{ marginTop: 12 }}>
            <Button
              type="primary"
              loading={replaceFormMapsMutation.isPending}
              onClick={() => void handleSave()}
            >
              {t('page.businessProcessAdmin.formMaps.edit.saveButton', { defaultValue: 'Save' })}
            </Button>
          </Space>
      </div>

      {/* Workflow visualization — full width, below the form-config block (more space) */}
      <div style={{ marginTop: 16 }}>
        {workflowDefinition && (workflowDefinition.states?.length ?? 0) > 0 ? (
          <Card size="small" styles={{ body: { padding: 0 } }}>
            <BpWorkflowFlowReadOnly
              states={workflowDefinition.states}
              transitions={workflowDefinition.transitions ?? []}
              height={460}
            />
          </Card>
        ) : (
          <Card
            size="small"
            style={{ minHeight: 360, display: 'flex', alignItems: 'center', justifyContent: 'center' }}
            styles={{ body: { width: '100%', textAlign: 'center', color: '#999' } }}
          >
            {t('page.businessProcessAdmin.workflowVisualizationUnavailable', {
              defaultValue: 'Workflow visualization unavailable',
            })}
          </Card>
        )}
      </div>
    </>
  );
}

// ─── Read-only sub-component ──────────────────────────────────────────────────

function ReadOnlyTabs({
  stateForms,
  transitionForms,
  t,
}: {
  stateForms: BpStateFormMapDto[];
  transitionForms: BpTransitionFormMapDto[];
  t: (key: string, opts?: Record<string, unknown>) => string;
}) {
  const stateColumns: ColumnsType<BpStateFormMapDto> = [
    { title: t('page.businessProcessAdmin.formMaps.col.stateId', { defaultValue: 'State ID' }), dataIndex: 'workflowStateId', key: 'workflowStateId', width: 100 },
    { title: t('page.businessProcessAdmin.formMaps.col.formTemplateId', { defaultValue: 'Form Template ID' }), dataIndex: 'formTemplateId', key: 'formTemplateId', width: 140 },
    { title: t('page.businessProcessAdmin.formMaps.col.version', { defaultValue: 'Version' }), dataIndex: 'formTemplateVersion', key: 'formTemplateVersion', width: 80 },
    {
      title: t('page.businessProcessAdmin.formMaps.col.required', { defaultValue: 'Required' }),
      dataIndex: 'isRequired',
      key: 'isRequired',
      width: 90,
      render: (v: boolean) => (
        <Tag color={v ? 'red' : 'default'}>
          {v ? t('common.yes', { defaultValue: 'Yes' }) : t('common.no', { defaultValue: 'No' })}
        </Tag>
      ),
    },
    { title: t('page.businessProcessAdmin.formMaps.col.order', { defaultValue: 'Order' }), dataIndex: 'displayOrder', key: 'displayOrder', width: 70 },
  ];

  const transitionColumns: ColumnsType<BpTransitionFormMapDto> = [
    { title: t('page.businessProcessAdmin.formMaps.col.transitionId', { defaultValue: 'Transition ID' }), dataIndex: 'workflowTransitionId', key: 'workflowTransitionId', width: 110 },
    { title: t('page.businessProcessAdmin.formMaps.col.formTemplateId', { defaultValue: 'Form Template ID' }), dataIndex: 'formTemplateId', key: 'formTemplateId', width: 140 },
    { title: t('page.businessProcessAdmin.formMaps.col.version', { defaultValue: 'Version' }), dataIndex: 'formTemplateVersion', key: 'formTemplateVersion', width: 80 },
    {
      title: t('page.businessProcessAdmin.formMaps.col.required', { defaultValue: 'Required' }),
      dataIndex: 'isRequired',
      key: 'isRequired',
      width: 90,
      render: (v: boolean) => (
        <Tag color={v ? 'red' : 'default'}>
          {v ? t('common.yes', { defaultValue: 'Yes' }) : t('common.no', { defaultValue: 'No' })}
        </Tag>
      ),
    },
    { title: t('page.businessProcessAdmin.formMaps.col.order', { defaultValue: 'Order' }), dataIndex: 'displayOrder', key: 'displayOrder', width: 70 },
  ];

  return (
    <Tabs
      items={[
        {
          key: 'state-forms',
          label: t('page.businessProcessAdmin.tabStateForms', { defaultValue: 'Form theo trạng thái' }),
          children: (
            <Table<BpStateFormMapDto>
              rowKey="id"
              dataSource={stateForms}
              columns={stateColumns}
              size="small"
              pagination={false}
              locale={{ emptyText: t('common.noData', { defaultValue: 'Không có dữ liệu' }) }}
            />
          ),
        },
        {
          key: 'transition-forms',
          label: t('page.businessProcessAdmin.tabTransitionForms', { defaultValue: 'Form theo chuyển trạng thái' }),
          children: (
            <Table<BpTransitionFormMapDto>
              rowKey="id"
              dataSource={transitionForms}
              columns={transitionColumns}
              size="small"
              pagination={false}
              locale={{ emptyText: t('common.noData', { defaultValue: 'Không có dữ liệu' }) }}
            />
          ),
        },
      ]}
    />
  );
}
