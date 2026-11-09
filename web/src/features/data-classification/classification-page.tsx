import { useState } from 'react';
import { Table, Tabs, Tag, Button, Statistic, Row, Col, Card } from 'antd';
import {
  CheckCircleOutlined,
  CloseCircleOutlined,
  EditOutlined,
  SafetyCertificateOutlined,
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { usePermissions } from '@/features/auth';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { useClassificationInventory, useClassificationRules } from './classification-api';
import { ClassificationRulesModal } from './classification-rules-modal';
import { DataClassificationLevel, type ClassifiedField, type ClassificationRule } from './classification-types';

// ─── Level badge config ───────────────────────────────────────────────────────

type LevelColor = 'default' | 'blue' | 'orange' | 'red';

const LEVEL_COLOR: Record<DataClassificationLevel, LevelColor> = {
  [DataClassificationLevel.Public]: 'default',
  [DataClassificationLevel.Internal]: 'blue',
  [DataClassificationLevel.Confidential]: 'orange',
  [DataClassificationLevel.Restricted]: 'red',
};

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * ClassificationPage — QĐ 742 §6.2.a data classification admin.
 * Route: /admin/data-classification
 * Required permission: COMPLIANCE.CLASSIFICATION.VIEW
 * Edit rules requires: COMPLIANCE.CLASSIFICATION.MANAGE
 */
export function ClassificationPage() {
  const { t } = useTranslation();
  const { hasPermission } = usePermissions();
  const canManage = hasPermission('COMPLIANCE.CLASSIFICATION.MANAGE');

  const [editingRule, setEditingRule] = useState<ClassificationRule | null>(null);
  const [modalOpen, setModalOpen] = useState(false);

  const { data: inventory, isLoading: inventoryLoading } = useClassificationInventory();
  const { data: rules, isLoading: rulesLoading } = useClassificationRules();

  // ── Level label helper ────────────────────────────────────────────────────

  function levelLabel(level: DataClassificationLevel): string {
    const map: Record<DataClassificationLevel, string> = {
      [DataClassificationLevel.Public]: t('dataClassification.levels.public'),
      [DataClassificationLevel.Internal]: t('dataClassification.levels.internal'),
      [DataClassificationLevel.Confidential]: t('dataClassification.levels.confidential'),
      [DataClassificationLevel.Restricted]: t('dataClassification.levels.restricted'),
    };
    return map[level];
  }

  // ── Modal handlers ────────────────────────────────────────────────────────

  function openEdit(rule: ClassificationRule) {
    setEditingRule(rule);
    setModalOpen(true);
  }

  function closeModal() {
    setModalOpen(false);
    setEditingRule(null);
  }

  // ── Inventory table columns ───────────────────────────────────────────────

  const inventoryColumns: ColumnsType<ClassifiedField> = [
    {
      title: t('dataClassification.columns.assembly'),
      dataIndex: 'assemblyName',
      key: 'assemblyName',
      ellipsis: true,
      width: 220,
      render: (v: string) => {
        // Shorten assembly name: take last segment after last dot
        const short = v.split('.').pop() ?? v;
        return <span title={v}>{short}</span>;
      },
    },
    {
      title: t('dataClassification.columns.type'),
      dataIndex: 'typeFullName',
      key: 'typeFullName',
      ellipsis: true,
      width: 200,
      render: (v: string) => {
        const short = v.split('.').pop() ?? v;
        return <span title={v}>{short}</span>;
      },
    },
    {
      title: t('dataClassification.columns.property'),
      dataIndex: 'propertyName',
      key: 'propertyName',
      width: 160,
    },
    {
      title: t('dataClassification.columns.propertyType'),
      dataIndex: 'propertyType',
      key: 'propertyType',
      width: 120,
      render: (v: string) => <code style={{ fontSize: 12 }}>{v}</code>,
    },
    {
      title: t('dataClassification.columns.level'),
      dataIndex: 'level',
      key: 'level',
      width: 130,
      filters: [
        { text: t('dataClassification.levels.public'), value: DataClassificationLevel.Public },
        { text: t('dataClassification.levels.internal'), value: DataClassificationLevel.Internal },
        { text: t('dataClassification.levels.confidential'), value: DataClassificationLevel.Confidential },
        { text: t('dataClassification.levels.restricted'), value: DataClassificationLevel.Restricted },
      ],
      onFilter: (value, record) => record.level === value,
      render: (level: DataClassificationLevel) => (
        <Tag color={LEVEL_COLOR[level]}>{levelLabel(level)}</Tag>
      ),
    },
    {
      title: t('dataClassification.columns.encrypted'),
      dataIndex: 'isEncryptedAtRest',
      key: 'isEncryptedAtRest',
      width: 100,
      align: 'center',
      render: (v: boolean) =>
        v
          ? <CheckCircleOutlined style={{ color: '#52c41a', fontSize: 16 }} />
          : <CloseCircleOutlined style={{ color: '#d9d9d9', fontSize: 16 }} />,
    },
  ];

  // ── Rules table columns ───────────────────────────────────────────────────

  const rulesColumns: ColumnsType<ClassificationRule> = [
    {
      title: t('dataClassification.columns.level'),
      dataIndex: 'level',
      key: 'level',
      width: 140,
      render: (level: DataClassificationLevel) => (
        <Tag color={LEVEL_COLOR[level]}>{levelLabel(level)}</Tag>
      ),
    },
    {
      title: t('dataClassification.rules.mustEncrypt'),
      dataIndex: 'mustEncryptAtRest',
      key: 'mustEncryptAtRest',
      width: 140,
      align: 'center',
      render: (v: boolean) =>
        v
          ? <Tag color="red">{t('common.yes')}</Tag>
          : <Tag color="default">{t('common.no')}</Tag>,
    },
    {
      title: t('dataClassification.rules.allowExport'),
      dataIndex: 'allowExport',
      key: 'allowExport',
      width: 130,
      align: 'center',
      render: (v: boolean) =>
        v
          ? <Tag color="green">{t('common.yes')}</Tag>
          : <Tag color="orange">{t('common.no')}</Tag>,
    },
    {
      title: t('dataClassification.rules.maxRetentionDays'),
      dataIndex: 'maxRetentionDays',
      key: 'maxRetentionDays',
      width: 160,
      render: (v: number | null) =>
        v == null
          ? <span style={{ color: 'var(--gov-text-muted)' }}>{t('dataClassification.rules.unlimited')}</span>
          : `${v} ${t('dataClassification.rules.days')}`,
    },
    {
      title: t('dataClassification.rules.updatedBy'),
      dataIndex: 'updatedBy',
      key: 'updatedBy',
      ellipsis: true,
    },
    {
      title: t('dataClassification.rules.updatedAt'),
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 130,
      render: (v: string) => new Date(v).toLocaleDateString('vi-VN'),
    },
    {
      title: '',
      key: 'actions',
      width: 60,
      render: (_: unknown, record: ClassificationRule) => (
        <Button
          size="small"
          icon={<EditOutlined />}
          onClick={() => openEdit(record)}
          title={canManage ? t('dataClassification.rules.edit') : t('common.close')}
        />
      ),
    },
  ];

  // ── Render ────────────────────────────────────────────────────────────────

  const scannedAt = inventory?.scannedAt
    ? new Date(inventory.scannedAt).toLocaleString('vi-VN')
    : '-';

  return (
    <div>
      <AdminPageHeader
        title={t('dataClassification.title')}
        description={t('dataClassification.summary.scannedAt', { time: scannedAt })}
        icon={<SafetyCertificateOutlined />}
      />

      {/* Summary stat chips */}
      <AdminContentCard>
        <Row gutter={24}>
          <Col xs={24} sm={8}>
            <Statistic
              title={t('dataClassification.summary.total')}
              value={inventory?.totalCount ?? '-'}
              loading={inventoryLoading}
            />
          </Col>
          <Col xs={24} sm={8}>
            <Statistic
              title={t('dataClassification.summary.confidential')}
              value={inventory?.confidentialCount ?? '-'}
              valueStyle={{ color: '#fa8c16' }}
              loading={inventoryLoading}
            />
          </Col>
          <Col xs={24} sm={8}>
            <Statistic
              title={t('dataClassification.summary.restricted')}
              value={inventory?.restrictedCount ?? '-'}
              valueStyle={{ color: '#cf1322' }}
              loading={inventoryLoading}
            />
          </Col>
        </Row>
      </AdminContentCard>

      {/* Tabs: inventory + rules */}
      <Card
        variant="borderless"
        style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)', marginTop: 'var(--page-gap)' }}
        styles={{ body: { padding: 0 } }}
      >
        <Tabs
          defaultActiveKey="inventory"
          style={{ padding: '0 16px' }}
          items={[
            {
              key: 'inventory',
              label: t('dataClassification.tabs.inventory'),
              children: (
                <Table<ClassifiedField>
                  rowKey={(r) => `${r.assemblyName}::${r.typeFullName}::${r.propertyName}`}
                  columns={inventoryColumns}
                  dataSource={inventory?.entries ?? []}
                  loading={inventoryLoading}
                  size="small"
                  pagination={{ pageSize: 50, showSizeChanger: false }}
                  style={{ padding: '0 0 8px' }}
                  scroll={{ x: 900 }}
                />
              ),
            },
            {
              key: 'rules',
              label: t('dataClassification.tabs.rules'),
              children: (
                <Table<ClassificationRule>
                  rowKey="level"
                  columns={rulesColumns}
                  dataSource={rules ?? []}
                  loading={rulesLoading}
                  size="small"
                  pagination={false}
                  style={{ padding: '0 0 8px' }}
                />
              ),
            },
          ]}
        />
      </Card>

      {/* Edit/view modal */}
      <ClassificationRulesModal
        rule={editingRule}
        open={modalOpen}
        canManage={canManage}
        onClose={closeModal}
      />
    </div>
  );
}
