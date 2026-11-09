// Plan 260523-1241 Phase 06c — TransitionAssignmentEditor.
//
// Controlled-component editor for `AssignmentInput` rendered inside the properties-sidebar
// edge drawer. Wired by parent <Form.Item name="assignment" valuePropName="value">.
//
// MVP modes per Q6 ratification + RT2-15 Phase 01-04 (2026-05-24→2026-05-26):
//   - SpecificUser    (0): refsJson = JSON array of user ids, picked via WorkflowUserMultiSelect (06b)
//   - Initiator       (1): refsJson stays null; no refs editor rendered
//   - PreviousHandler (2): refsJson stays null; no refs editor rendered (auto-resolved at runtime
//                          by IWorkflowHandlerResolver from IWorkflowTaskRepository.GetLastCompletedTaskAsync)
//   - ByPermission    (3): refsJson = JSON array of PermCode strings, picked via useWorkflowPermCodes (06b)
//   - ByRole          (4): refsJson = JSON array of role codes (canonical UPPER, e.g. "BTC_MINISTER"),
//                          picked via WorkflowRoleMultiSelect (predicate-pool).
//   - ByGroup         (5): refsJson = JSON array of group codes (canonical UPPER, e.g. "GROUP_TEST_A").
//                          RT2-15 Phase 02 — predicate-pool.
//   - ByOrgUnit       (6): refsJson = JSON array of org-unit codes (canonical UPPER, e.g. "OU_PHONG_TIEP_NHAN").
//                          RT2-15 Phase 04 — temporal predicate-pool; active-membership
//                          filter enforced at claim/inbox time via IOrgUnitModuleClient.GetUserOrgUnitCodesAsync.
//
// Still rejected server-side: CombineMode=Intersect=1 (no MVP producer).
//
// Why Collapse: keeps drawer height usable when no assignment is set. defaultActiveKey
// expands automatically when an assignment payload already exists (edit flow).

import { useMemo } from 'react';
import { Checkbox, Collapse, Select, Space, Button, Form } from 'antd';
import { useTranslation } from 'react-i18next';
import type {
  AssignmentInput,
  AssignmentClauseInput,
  AssignmentClauseMode,
  AssignmentCombineMode,
} from '../workflow-types';
import { useWorkflowPermCodes } from '../hooks/use-perm-codes';
import { WorkflowUserMultiSelect } from './workflow-user-multi-select';
import { WorkflowRoleMultiSelect } from './workflow-role-multi-select';
import { WorkflowGroupMultiSelect } from './workflow-group-multi-select';
import { WorkflowOrgUnitMultiSelect } from './workflow-org-unit-multi-select';

const COMBINE_MODE_UNION: AssignmentCombineMode = 'Union';
const CLAUSE_MODE_OPTIONS: { value: AssignmentClauseMode; labelKey: string; fallback: string }[] = [
  { value: 'SpecificUser', labelKey: 'workflow.assignment.modeSpecificUser', fallback: 'Người dùng cụ thể' },
  { value: 'Initiator', labelKey: 'workflow.assignment.modeInitiator', fallback: 'Người khởi tạo' },
  { value: 'PreviousHandler', labelKey: 'workflow.assignment.modePreviousHandler', fallback: 'Người xử lý trước' },
  { value: 'ByPermission', labelKey: 'workflow.assignment.modeByPermission', fallback: 'Theo quyền' },
  { value: 'ByRole', labelKey: 'workflow.assignment.modeByRole', fallback: 'Theo vai trò' },
  { value: 'ByGroup', labelKey: 'workflow.assignment.modeByGroup', fallback: 'Theo nhóm người dùng' },
  { value: 'ByOrgUnit', labelKey: 'workflow.assignment.modeByOrgUnit', fallback: 'Theo đơn vị tổ chức' },
];

export interface TransitionAssignmentEditorProps {
  value?: AssignmentInput | null;
  onChange?: (next: AssignmentInput | null) => void;
  /**
   * Restrict the selectable clause modes. Undefined ⇒ all modes (transition usage: the assignee
   * is resolved directly, so any mode is safe). A caller passes a subset only when every other
   * mode would produce an UNCLAIMABLE pool — ByGroup/ByOrgUnit have no shipped claim/inbox
   * read-side (see WorkflowHandlerResolver ByGroup note), so a pool in those modes can never be
   * picked up; for an AND-join that strands the whole branch. Branch-child editors therefore
   * limit to the read-side-complete modes (ByPermission + ByRole).
   */
  allowedModes?: AssignmentClauseMode[];
}

function parseRefsJson(refsJson: string | null): string[] {
  if (!refsJson) return [];
  try {
    const parsed = JSON.parse(refsJson) as unknown;
    return Array.isArray(parsed) ? (parsed.filter((x) => typeof x === 'string') as string[]) : [];
  } catch {
    return [];
  }
}

function stringifyRefs(refs: string[]): string | null {
  return refs.length > 0 ? JSON.stringify(refs) : null;
}

export function TransitionAssignmentEditor({ value, onChange, allowedModes }: TransitionAssignmentEditorProps) {
  const { t } = useTranslation();
  const { codes: permCodes } = useWorkflowPermCodes();

  const permOptions = useMemo(
    () => permCodes.map((c) => ({ value: c, label: c })),
    [permCodes]
  );

  // Modes offered in the clause-type Select; restricted set when allowedModes is provided.
  const clauseModeOptions = useMemo(
    () => (allowedModes ? CLAUSE_MODE_OPTIONS.filter((o) => allowedModes.includes(o.value)) : CLAUSE_MODE_OPTIONS),
    [allowedModes]
  );

  // Local helpers — always emit a fresh AssignmentInput so React diffs detect change.
  const emit = (next: AssignmentInput) => onChange?.(next);

  const current: AssignmentInput = value ?? {
    combineMode: COMBINE_MODE_UNION,
    allowAssignorOverride: false,
    clauses: [],
  };

  const updateClause = (idx: number, patch: Partial<AssignmentClauseInput>) => {
    const nextClauses = current.clauses.map((c, i) => (i === idx ? { ...c, ...patch } : c));
    emit({ ...current, clauses: nextClauses });
  };

  const removeClause = (idx: number) => {
    const nextClauses = current.clauses.filter((_, i) => i !== idx);
    // M2 fold (code-review 260523-1730): empty clauses + no override → emit null per
    // Q2 replace-all DELETE semantic. Avoids hitting Phase 05 M2 pending BE R5 gap
    // where {clauses:[]} would persist a no-clause assignment row.
    if (nextClauses.length === 0 && !current.allowAssignorOverride) {
      onChange?.(null);
      return;
    }
    emit({ ...current, clauses: nextClauses });
  };

  const addClause = () => {
    // Default to a mode that is actually selectable — else a restricted editor would seed a
    // clause whose mode the Select can't render (invalid rc-select value).
    const defaultMode: AssignmentClauseMode = allowedModes?.[0] ?? 'Initiator';
    const nextClause: AssignmentClauseInput = { mode: defaultMode, refsJson: null };
    emit({ ...current, clauses: [...current.clauses, nextClause] });
  };

  const toggleOverride = (checked: boolean) => {
    emit({ ...current, allowAssignorOverride: checked });
  };

  const defaultActiveKey = value && (value.clauses.length > 0 || value.allowAssignorOverride) ? ['assign'] : [];

  return (
    <Collapse
      ghost
      defaultActiveKey={defaultActiveKey}
      items={[
        {
          key: 'assign',
          label: t('workflow.assignment.title', 'Phân quyền hành động'),
          children: (
            <Space direction="vertical" style={{ width: '100%' }} size="small">
              <Form.Item style={{ marginBottom: 8 }}>
                <Checkbox
                  checked={current.allowAssignorOverride}
                  onChange={(e) => toggleOverride(e.target.checked)}
                >
                  {t('workflow.assignment.allowOverride', 'Cho phép người giao việc override')}
                </Checkbox>
              </Form.Item>

              {current.clauses.map((clause, idx) => (
                <div
                  key={idx}
                  data-testid={`clause-row-${idx}`}
                  style={{ border: '1px dashed #d9d9d9', padding: 8, borderRadius: 4 }}
                >
                  <Space direction="vertical" style={{ width: '100%' }} size={4}>
                    <Select
                      value={clause.mode}
                      onChange={(mode) => updateClause(idx, { mode, refsJson: null })}
                      options={clauseModeOptions.map((o) => ({ value: o.value, label: t(o.labelKey, o.fallback) }))}
                      style={{ width: '100%' }}
                    />

                    {clause.mode === 'ByPermission' && (
                      <Select
                        data-testid={`clause-row-${idx}-perm-codes`}
                        mode="multiple"
                        showSearch
                        value={parseRefsJson(clause.refsJson)}
                        onChange={(refs: string[]) => updateClause(idx, { refsJson: stringifyRefs(refs) })}
                        options={permOptions}
                        placeholder={t('workflow.assignment.permCodesPlaceholder', 'Chọn quyền')}
                        style={{ width: '100%' }}
                      />
                    )}

                    {clause.mode === 'SpecificUser' && (
                      <div data-testid={`clause-row-${idx}-user-picker`}>
                        <WorkflowUserMultiSelect
                          value={parseRefsJson(clause.refsJson)}
                          onChange={(refs) => updateClause(idx, { refsJson: stringifyRefs(refs) })}
                          placeholder={t('workflow.assignment.userPlaceholder', 'Chọn người dùng')}
                        />
                      </div>
                    )}

                    {clause.mode === 'ByRole' && (
                      <div data-testid={`clause-row-${idx}-role-picker`}>
                        <WorkflowRoleMultiSelect
                          value={parseRefsJson(clause.refsJson)}
                          onChange={(refs) => updateClause(idx, { refsJson: stringifyRefs(refs) })}
                          placeholder={t('workflow.assignment.rolePlaceholder', 'Chọn vai trò')}
                        />
                      </div>
                    )}

                    {clause.mode === 'ByGroup' && (
                      <div data-testid={`clause-row-${idx}-group-picker`}>
                        <WorkflowGroupMultiSelect
                          value={parseRefsJson(clause.refsJson)}
                          onChange={(refs) => updateClause(idx, { refsJson: stringifyRefs(refs) })}
                          placeholder={t('workflow.assignment.groupPlaceholder', 'Chọn nhóm người dùng...')}
                        />
                      </div>
                    )}

                    {clause.mode === 'ByOrgUnit' && (
                      <div data-testid={`clause-row-${idx}-org-unit-picker`}>
                        <WorkflowOrgUnitMultiSelect
                          value={parseRefsJson(clause.refsJson)}
                          onChange={(refs) => updateClause(idx, { refsJson: stringifyRefs(refs) })}
                          placeholder={t('workflow.assignment.orgUnitPlaceholder', 'Chọn đơn vị tổ chức...')}
                        />
                      </div>
                    )}

                    <Button size="small" danger onClick={() => removeClause(idx)}>
                      {t('common.remove', 'Xoá')}
                    </Button>
                  </Space>
                </div>
              ))}

              <Button type="dashed" onClick={addClause} block size="small">
                + {t('workflow.assignment.addClause', 'Thêm clause')}
              </Button>
            </Space>
          ),
        },
      ]}
    />
  );
}
