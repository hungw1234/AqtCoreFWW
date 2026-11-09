// workflow-state-mapping-row — single row in the state mapping wizard table.
// Renders one old state with its active instance count badge and a dropdown to pick the new state.

import { Select, Badge, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { WorkflowStateDto } from '../workflow-types';

const { Text } = Typography;

interface Props {
  oldState: WorkflowStateDto;
  /** Active instance count for this old state (from UnmappedStateDto or pre-fetched) */
  instanceCount: number;
  /** Currently selected new state ID (undefined = unmapped) */
  newStateId: string | undefined;
  targetStates: WorkflowStateDto[];
  onChange: (oldStateId: string, newStateId: string | undefined) => void;
  /** Read-only mode — shown after migration created */
  readOnly?: boolean;
  /** Resolved new state name when read-only */
  resolvedNewStateName?: string;
}

export function WorkflowStateMappingRow({
  oldState,
  instanceCount,
  newStateId,
  targetStates,
  onChange,
  readOnly,
  resolvedNewStateName,
}: Props) {
  const { t } = useTranslation();

  return (
    <Space style={{ width: '100%', justifyContent: 'space-between' }} align="center">
      {/* Left: old state name + active instance badge */}
      <Space>
        <Text strong>{oldState.name}</Text>
        {instanceCount > 0 && (
          <Badge
            count={instanceCount}
            style={{ backgroundColor: '#faad14' }}
            title={t('workflow.migration.field.instanceCount')}
          />
        )}
      </Space>

      {/* Right: target state dropdown or read-only name */}
      {readOnly ? (
        <Text type="secondary">{resolvedNewStateName ?? '—'}</Text>
      ) : (
        <Select
          style={{ minWidth: 200 }}
          placeholder={t('workflow.selectState')}
          value={newStateId}
          allowClear
          onChange={(val) => onChange(oldState.id, val)}
          options={targetStates.map((s) => ({ label: s.name, value: s.id }))}
        />
      )}
    </Space>
  );
}
