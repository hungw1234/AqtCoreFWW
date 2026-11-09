import { useEffect } from 'react';
import { Modal, Form, Switch, InputNumber, Descriptions } from 'antd';
import { useTranslation } from 'react-i18next';
import { useUpdateClassificationRule } from './classification-api';
import { DataClassificationLevel, type ClassificationRule, type UpdateClassificationRuleRequest } from './classification-types';
import { appMessage } from '@/core/api/notification-bridge';

// ─── Props ────────────────────────────────────────────────────────────────────

interface ClassificationRulesModalProps {
  rule: ClassificationRule | null;
  open: boolean;
  canManage: boolean; // true if user has COMPLIANCE.CLASSIFICATION.MANAGE
  onClose: () => void;
}

// ─── Form values shape ────────────────────────────────────────────────────────

interface RuleFormValues {
  mustEncryptAtRest: boolean;
  allowExport: boolean;
  maxRetentionDays: number | null;
}

// ─── Level label helper ───────────────────────────────────────────────────────

function useLevelLabel(level: DataClassificationLevel | undefined, t: (key: string) => string): string {
  if (level === undefined) return '';
  const map: Record<DataClassificationLevel, string> = {
    [DataClassificationLevel.Public]: t('dataClassification.levels.public'),
    [DataClassificationLevel.Internal]: t('dataClassification.levels.internal'),
    [DataClassificationLevel.Confidential]: t('dataClassification.levels.confidential'),
    [DataClassificationLevel.Restricted]: t('dataClassification.levels.restricted'),
  };
  return map[level];
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * ClassificationRulesModal — edit handling rules for one DataClassificationLevel.
 * Read-only when canManage=false (Compliance.View only); save button hidden.
 * On 409 Conflict (RowVersion mismatch): shows concurrency warning + reloads.
 */
export function ClassificationRulesModal({ rule, open, canManage, onClose }: ClassificationRulesModalProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<RuleFormValues>();
  const updateRule = useUpdateClassificationRule();
  const levelLabel = useLevelLabel(rule?.level, t);

  // Populate form when rule changes (different level selected for edit)
  useEffect(() => {
    if (rule && open) {
      form.setFieldsValue({
        mustEncryptAtRest: rule.mustEncryptAtRest,
        allowExport: rule.allowExport,
        maxRetentionDays: rule.maxRetentionDays,
      });
    }
  }, [rule, open, form]);

  async function handleSave() {
    if (!rule) return;

    let values: RuleFormValues;
    try {
      values = await form.validateFields();
    } catch {
      return; // antd shows inline validation errors
    }

    const body: UpdateClassificationRuleRequest = {
      mustEncryptAtRest: values.mustEncryptAtRest,
      allowExport: values.allowExport,
      maxRetentionDays: values.maxRetentionDays ?? null,
      rowVersion: rule.rowVersion,
    };

    try {
      await updateRule.mutateAsync({ level: rule.level, body });
      appMessage.success(t('common.success'));
      onClose();
    } catch (err: unknown) {
      // 409 = RowVersion mismatch — another admin updated concurrently
      const status = (err as { status?: number })?.status;
      if (status === 409) {
        appMessage.error(t('dataClassification.rules.modal.concurrency'));
        onClose(); // close so parent can refetch + user re-opens with fresh data
      } else {
        appMessage.error(t('common.failed'));
      }
    }
  }

  function handleCancel() {
    form.resetFields();
    onClose();
  }

  return (
    <Modal
      title={t('dataClassification.rules.modal.title', { level: levelLabel })}
      open={open}
      onOk={canManage ? handleSave : undefined}
      onCancel={handleCancel}
      confirmLoading={updateRule.isPending}
      okText={canManage ? t('dataClassification.rules.save') : undefined}
      cancelText={t('dataClassification.rules.cancel')}
      // Hide footer OK button for read-only users — they can still view values
      footer={canManage ? undefined : null}
      destroyOnHidden
      width={480}
    >
      {/* Read-only metadata */}
      <Descriptions size="small" column={1} style={{ marginBottom: 16 }}>
        <Descriptions.Item label={t('dataClassification.rules.updatedBy')}>
          {rule?.updatedBy ?? '-'}
        </Descriptions.Item>
        <Descriptions.Item label={t('dataClassification.rules.updatedAt')}>
          {rule?.updatedAt ? new Date(rule.updatedAt).toLocaleDateString('vi-VN') : '-'}
        </Descriptions.Item>
      </Descriptions>

      <Form form={form} layout="vertical" disabled={!canManage}>
        <Form.Item
          name="mustEncryptAtRest"
          label={t('dataClassification.rules.mustEncrypt')}
          valuePropName="checked"
        >
          <Switch
            checkedChildren={t('common.yes')}
            unCheckedChildren={t('common.no')}
          />
        </Form.Item>

        <Form.Item
          name="allowExport"
          label={t('dataClassification.rules.allowExport')}
          valuePropName="checked"
        >
          <Switch
            checkedChildren={t('common.yes')}
            unCheckedChildren={t('common.no')}
          />
        </Form.Item>

        <Form.Item
          name="maxRetentionDays"
          label={t('dataClassification.rules.maxRetentionDays')}
          tooltip={t('dataClassification.rules.maxRetentionDaysHelp')}
        >
          <InputNumber
            min={1}
            max={36500}
            placeholder={t('dataClassification.rules.unlimited')}
            style={{ width: '100%' }}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
}
