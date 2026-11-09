// Branch editor — AntD form rendered inside the properties sidebar when a gateway node
// is selected. Controlled component: emits a full updated WorkflowBranchDto on every change.
// Sub-components (ChildRow, ResolutionRow, SingleUserSelect) live in branch-editor-child-row.tsx.

import { Select, InputNumber, Switch, Button, Space, Divider } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { ChildRow } from './branch-editor-child-row';
import type {
  WorkflowBranchDto,
  WorkflowBranchChildDto,
  WorkflowStateDto,
  BranchType,
} from '../workflow-types';

const BRANCH_TYPE_OPTIONS: { value: BranchType; labelKey: string; fallback: string }[] = [
  { value: 'And',  labelKey: 'workflow.branch.typeAnd',  fallback: 'AND — tất cả phải duyệt' },
  { value: 'Or',   labelKey: 'workflow.branch.typeOr',   fallback: 'OR — một trong số phải duyệt' },
  { value: 'NOfM', labelKey: 'workflow.branch.typeNOfM', fallback: 'N/M — N trong M phải duyệt' },
];

export interface BranchEditorProps {
  value: WorkflowBranchDto;
  states: WorkflowStateDto[];
  onChange: (b: WorkflowBranchDto) => void;
  /** State ids that already own a *different* branch (RB3). Excluded from the branching-state
   *  picker so re-pointing a fork can't collide with an existing one. Defaults to empty. */
  branchedStateIds?: Set<string>;
}

export function BranchEditor({ value, states, onChange, branchedStateIds }: BranchEditorProps) {
  const { t } = useTranslation();
  const stateOpts = states.map((s) => ({ value: s.id, label: s.displayNameVi }));

  // Branching-state options: a fork may sit on any non-final state (FE UX choice) that doesn't
  // already own another branch (RB3 — one fork per state). The current branching state stays
  // selectable so editing other fields doesn't clear it. Mirrors the per-node canvas forkable rule.
  const owned = branchedStateIds ?? new Set<string>();
  const branchingStateOpts = states
    .filter((s) => !s.isFinal && (!owned.has(s.id) || s.id === value.branchingStateId))
    .map((s) => ({ value: s.id, label: s.displayNameVi }));

  const emit = (patch: Partial<WorkflowBranchDto>) => onChange({ ...value, ...patch });

  const addChild = () => {
    const newChild: WorkflowBranchChildDto = {
      id: crypto.randomUUID(),
      targetStateId: '',
      name: 'NewBranch',
      displayNameVi: 'Nhánh mới',
      sortOrder: value.children.length,
      resolutions: [],
    };
    emit({ children: [...value.children, newChild] });
  };

  const updateChild = (i: number, c: WorkflowBranchChildDto) =>
    emit({ children: value.children.map((x, idx) => (idx === i ? c : x)) });

  const removeChild = (i: number) =>
    emit({ children: value.children.filter((_, idx) => idx !== i) });

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="small">
      <div>
        <div style={{ fontSize: 11, color: '#888', marginBottom: 4 }}>{t('workflow.branch.branchingState', 'Bước rẽ nhánh (fork)')}</div>
        <Select size="small" style={{ width: '100%' }} value={value.branchingStateId || undefined}
          onChange={(v) => emit({ branchingStateId: v })}
          options={branchingStateOpts} placeholder={t('workflow.selectState', 'Chọn bước...')}
        />
      </div>

      <div>
        <div style={{ fontSize: 11, color: '#888', marginBottom: 4 }}>{t('workflow.branch.type', 'Kiểu nhánh')}</div>
        <Select size="small" style={{ width: '100%' }} value={value.branchType}
          onChange={(v) => emit({ branchType: v, requiredCount: v === 'NOfM' ? (value.requiredCount ?? 1) : null })}
          options={BRANCH_TYPE_OPTIONS.map((o) => ({ value: o.value, label: t(o.labelKey, o.fallback) }))}
        />
      </div>

      {value.branchType === 'NOfM' && (
        <div>
          <div style={{ fontSize: 11, color: '#888', marginBottom: 4 }}>{t('workflow.branch.requiredCount', 'Số lượng N cần duyệt')}</div>
          <InputNumber size="small" min={1} max={Math.max(1, value.children.length)} style={{ width: '100%' }}
            value={value.requiredCount ?? 1}
            onChange={(v) => emit({ requiredCount: v ?? 1 })}
          />
        </div>
      )}

      <div>
        <div style={{ fontSize: 11, color: '#888', marginBottom: 4 }}>{t('workflow.branch.continuationState', 'Bước hội tụ (join)')}</div>
        <Select size="small" style={{ width: '100%' }} value={value.continuationStateId || undefined}
          onChange={(v) => emit({ continuationStateId: v })}
          options={stateOpts} placeholder={t('workflow.selectState', 'Chọn bước...')}
        />
      </div>

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <span style={{ fontSize: 11, color: '#888' }}>{t('workflow.branch.allowCancelOnAnyReject', 'Hủy khi có nhánh từ chối')}</span>
        <Switch size="small" checked={value.allowCancelOnAnyReject}
          onChange={(v) => emit({ allowCancelOnAnyReject: v })}
        />
      </div>

      <div>
        <div style={{ fontSize: 11, color: '#888', marginBottom: 4 }}>{t('workflow.branch.timeoutMinutes', 'Timeout (phút)')}</div>
        <InputNumber size="small" min={0} style={{ width: '100%' }}
          value={value.timeoutMinutes}
          onChange={(v) => emit({ timeoutMinutes: v ?? 0 })}
        />
      </div>

      <Divider style={{ margin: '8px 0' }} />
      <div style={{ fontSize: 11, fontWeight: 600, marginBottom: 4 }}>{t('workflow.branch.children', 'Các nhánh con')}</div>

      {value.children.map((child, i) => (
        <ChildRow key={child.id} child={child} states={states} index={i}
          onChange={(c) => updateChild(i, c)}
          onRemove={() => removeChild(i)}
        />
      ))}

      <Button type="dashed" icon={<PlusOutlined />} onClick={addChild} block size="small">
        {t('workflow.branch.addChild', 'Thêm nhánh con')}
      </Button>
    </Space>
  );
}
