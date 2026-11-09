// workflow-state-mapping-wizard — 2-column mapping UI: old states → new states.
// Validate → Create flow. Locked (read-only) once migration is created (status != Draft).

import { useState } from 'react';
import { Button, Alert, Checkbox, Space, Typography, Divider, Tag, Spin } from 'antd';
import { CheckCircleOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { useWorkflowDefinition } from '../workflow-api';
import { useValidateMapping, useCreateMigration } from '../workflow-migration-api';
import { WorkflowStateMappingRow } from './workflow-state-mapping-row';
import type { MigrationDto, MigrationValidationResult, StateMappingDto } from '../workflow-types';
import { appMessage } from '@/core/api/notification-bridge';

const { Title, Text } = Typography;

interface Props {
  sourceDefinitionId: string;
  targetDefinitionId: string;
  definitionKey: string;
  /** Called after successful migration creation */
  onCreated: (migration: MigrationDto) => void;
}

export function WorkflowStateMappingWizard({
  sourceDefinitionId,
  targetDefinitionId,
  definitionKey,
  onCreated,
}: Props) {
  const { t } = useTranslation();

  const { data: sourceDef, isLoading: loadingSrc } = useWorkflowDefinition(sourceDefinitionId);
  const { data: targetDef, isLoading: loadingTgt } = useWorkflowDefinition(targetDefinitionId);

  // mapping: oldStateId → newStateId
  const [mapping, setMapping] = useState<Record<string, string | undefined>>({});
  const [allowDowngrade, setAllowDowngrade] = useState(false);
  const [validationResult, setValidationResult] = useState<MigrationValidationResult | null>(null);
  const [createdMigration, setCreatedMigration] = useState<MigrationDto | null>(null);

  const validateMutation = useValidateMapping();
  const createMutation = useCreateMigration(definitionKey);

  if (loadingSrc || loadingTgt) return <Spin />;
  if (!sourceDef || !targetDef) return <Text type="danger">{t('common.notFound')}</Text>;

  // If migration already created → locked display
  if (createdMigration) {
    return (
      <Space direction="vertical" style={{ width: '100%' }}>
        <Alert
          type="success"
          icon={<CheckCircleOutlined />}
          showIcon
          message={t('workflow.migration.action.create')}
          description={
            <Text code>{createdMigration.mappingHash}</Text>
          }
        />
        {sourceDef.states.map((state) => {
          const newId = mapping[state.id];
          const newState = targetDef.states.find((s) => s.id === newId);
          return (
            <WorkflowStateMappingRow
              key={state.id}
              oldState={state}
              instanceCount={0}
              newStateId={newId}
              targetStates={targetDef.states}
              onChange={() => {}}
              readOnly
              resolvedNewStateName={newState?.name}
            />
          );
        })}
      </Space>
    );
  }

  const buildMappings = (): StateMappingDto[] =>
    sourceDef.states
      .filter((s) => mapping[s.id])
      .map((s) => {
        const newId = mapping[s.id]!;
        const newState = targetDef.states.find((t) => t.id === newId);
        return {
          oldStateId: s.id,
          oldStateName: s.name,
          newStateId: newId,
          newStateName: newState?.name ?? '',
        };
      });

  const handleValidate = async () => {
    const result = await validateMutation.mutateAsync({
      sourceDefinitionId,
      targetDefinitionId,
      mappings: buildMappings(),
      allowDowngrade,
    });
    setValidationResult(result);
  };

  const handleCreate = async () => {
    try {
      const migration = await createMutation.mutateAsync({
        sourceDefinitionId,
        targetDefinitionId,
        mappings: buildMappings(),
        allowDowngrade,
      });
      setCreatedMigration(migration);
      appMessage.success(t('workflow.migration.action.create'));
      onCreated(migration);
    } catch {
      appMessage.error(t('common.error'));
    }
  };

  const isValid = validationResult?.isValid === true;

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="middle">
      <Title level={5}>
        v{sourceDef.version} → v{targetDef.version}
      </Title>

      {/* State mapping rows */}
      {sourceDef.states.map((state) => (
        <WorkflowStateMappingRow
          key={state.id}
          oldState={state}
          instanceCount={
            validationResult?.unmappedStates.find((u) => u.stateId === state.id)
              ?.activeInstanceCount ?? 0
          }
          newStateId={mapping[state.id]}
          targetStates={targetDef.states}
          onChange={(oldId, newId) =>
            setMapping((prev) => ({ ...prev, [oldId]: newId }))
          }
        />
      ))}

      <Divider />

      <Checkbox
        checked={allowDowngrade}
        onChange={(e) => setAllowDowngrade(e.target.checked)}
      >
        {t('workflow.migration.error.downgrade')}
      </Checkbox>

      {/* Validation feedback */}
      {validationResult && !validationResult.isValid && (
        <Alert
          type="error"
          message={t('workflow.migration.error.unmappedStates')}
          description={
            <ul style={{ margin: 0, paddingLeft: 16 }}>
              {validationResult.errors.map((e, i) => (
                <li key={i}>{e}</li>
              ))}
              {validationResult.unmappedStates.map((u) => (
                <li key={u.stateId}>
                  {u.stateName}{' '}
                  <Tag color="orange">{u.activeInstanceCount} active</Tag>
                </li>
              ))}
            </ul>
          }
        />
      )}

      {validationResult?.isValid && (
        <Alert type="success" message={t('common.success')} showIcon />
      )}

      <Space>
        <Button
          onClick={handleValidate}
          loading={validateMutation.isPending}
        >
          {t('workflow.migration.action.validate')}
        </Button>
        <Button
          type="primary"
          disabled={!isValid}
          loading={createMutation.isPending}
          onClick={handleCreate}
        >
          {t('workflow.migration.action.create')}
        </Button>
      </Space>
    </Space>
  );
}
