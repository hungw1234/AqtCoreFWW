// Sub-components for branch-editor.tsx child rows and resolution rows.
// Extracted to keep branch-editor.tsx under the 200L ceiling.

import { useState, useEffect } from 'react';
import { Select, Button, Space } from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useUserSearch } from '@/features/collaboration/user-search-api';
import { TransitionAssignmentEditor } from './transition-assignment-editor';
import type {
  WorkflowBranchChildDto,
  WorkflowBranchResolutionDto,
  WorkflowStateDto,
  BranchResolutionType,
  AssignmentClauseMode,
} from '../workflow-types';

// Branch-child pools restrict to the clause modes with a shipped claim/inbox read-side
// (ByPermission + ByRole). Module-level so the editor's memoized option list keeps a stable ref.
const BRANCH_CHILD_ALLOWED_MODES: AssignmentClauseMode[] = ['ByPermission', 'ByRole'];

const RESOLUTION_TYPE_OPTIONS: { value: BranchResolutionType; labelKey: string; fallback: string }[] = [
  { value: 'Approved',  labelKey: 'workflow.branch.resApproved',  fallback: 'Đã duyệt' },
  { value: 'Rejected',  labelKey: 'workflow.branch.resRejected',  fallback: 'Từ chối' },
  { value: 'Escalated', labelKey: 'workflow.branch.resEscalated', fallback: 'Leo thang' },
  { value: 'Timeout',   labelKey: 'workflow.branch.resTimeout',   fallback: 'Hết giờ' },
];

// Single-user select reusing useUserSearch — mirrors WorkflowUserMultiSelect but emits one id.
export function SingleUserSelect({
  value,
  onChange,
  placeholder,
}: {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
}) {
  const [query, setQuery] = useState('');
  const [debounced, setDebounced] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setDebounced(query), 300);
    return () => clearTimeout(t);
  }, [query]);
  const { data, isLoading } = useUserSearch(debounced, debounced.length >= 2);
  const opts = (data ?? []).map((u) => ({ value: u.id, label: u.fullName }));
  return (
    <Select
      showSearch filterOption={false} allowClear size="small"
      value={value ?? undefined}
      onSearch={setQuery}
      onChange={(v: string | undefined) => onChange(v ?? null)}
      options={opts} loading={isLoading}
      placeholder={placeholder} style={{ width: '100%' }}
    />
  );
}

export function ResolutionRow({
  resolution, states, onChange, onRemove,
}: {
  resolution: WorkflowBranchResolutionDto;
  states: WorkflowStateDto[];
  onChange: (r: WorkflowBranchResolutionDto) => void;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  const stateOpts = states.map((s) => ({ value: s.id, label: s.displayNameVi }));
  return (
    <Space size={4} style={{ width: '100%', marginBottom: 4 }}>
      <Select size="small" style={{ width: 120 }} value={resolution.resolutionType}
        onChange={(v) => onChange({ ...resolution, resolutionType: v })}
        options={RESOLUTION_TYPE_OPTIONS.map((o) => ({ value: o.value, label: t(o.labelKey, o.fallback) }))}
      />
      <Select size="small" style={{ flex: 1, minWidth: 100 }} allowClear
        value={resolution.resultingStateId ?? undefined}
        onChange={(v: string | undefined) => onChange({ ...resolution, resultingStateId: v ?? null })}
        options={stateOpts}
        placeholder={t('workflow.branch.resultingState', 'Bước chuyển đến')}
      />
      <Button size="small" danger icon={<DeleteOutlined />} onClick={onRemove} />
    </Space>
  );
}

export function ChildRow({
  child, states, onChange, onRemove, index,
}: {
  child: WorkflowBranchChildDto;
  states: WorkflowStateDto[];
  onChange: (c: WorkflowBranchChildDto) => void;
  onRemove: () => void;
  index: number;
}) {
  const { t } = useTranslation();
  const stateOpts = states.map((s) => ({ value: s.id, label: s.displayNameVi }));

  const updateResolution = (ri: number, r: WorkflowBranchResolutionDto) =>
    onChange({ ...child, resolutions: child.resolutions.map((x, i) => (i === ri ? r : x)) });
  const removeResolution = (ri: number) =>
    onChange({ ...child, resolutions: child.resolutions.filter((_, i) => i !== ri) });
  const addResolution = () =>
    onChange({ ...child, resolutions: [...child.resolutions, { resolutionType: 'Approved', resultingStateId: null }] });

  const handleTargetChange = (stateId: string | undefined) => {
    const s = states.find((st) => st.id === (stateId ?? ''));
    onChange({
      ...child,
      targetStateId: stateId ?? child.targetStateId,
      name: s?.name ?? child.name,
      displayNameVi: s?.displayNameVi ?? child.displayNameVi,
    });
  };

  return (
    <div style={{ border: '1px dashed #d9d9d9', borderRadius: 4, padding: 8, marginBottom: 8 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
        <span style={{ fontSize: 11, color: '#888' }}>{t('workflow.branch.child', 'Nhánh')} {index + 1}</span>
        <Button size="small" danger icon={<DeleteOutlined />} onClick={onRemove} />
      </div>
      <Select size="small" style={{ width: '100%', marginBottom: 6 }}
        value={child.targetStateId || undefined}
        onChange={handleTargetChange}
        options={stateOpts}
        placeholder={t('workflow.branch.childTarget', 'Bước xử lý của nhánh')}
      />
      <SingleUserSelect
        value={child.defaultAssigneeId}
        onChange={(id) => onChange({ ...child, defaultAssigneeId: id })}
        placeholder={t('workflow.branch.childAssignee', 'Người phụ trách mặc định')}
      />
      <TransitionAssignmentEditor
        value={child.assignment}
        onChange={(assignment) => onChange({ ...child, assignment })}
        allowedModes={BRANCH_CHILD_ALLOWED_MODES}
      />
      {child.resolutions.map((r, ri) => (
        <ResolutionRow key={ri} resolution={r} states={states}
          onChange={(updated) => updateResolution(ri, updated)}
          onRemove={() => removeResolution(ri)}
        />
      ))}
      <Button size="small" type="dashed" icon={<PlusOutlined />} onClick={addResolution} style={{ marginTop: 4 }}>
        {t('workflow.branch.addResolution', 'Thêm kết quả')}
      </Button>
    </div>
  );
}
