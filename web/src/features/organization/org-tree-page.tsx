import { useState, useMemo, useEffect, useRef } from 'react';
import { Tree, Button, Space, Typography, Descriptions, Card, Tag, Row, Col } from 'antd';
import { CenteredSpin } from '@/shared/components/centered-spin';
import { PlusOutlined, EditOutlined } from '@ant-design/icons';
import type { TreeProps, TreeDataNode } from 'antd';
import { useTranslation } from 'react-i18next';
import { useOrgUnits } from './org-api';
import { OrgUnitFormModal } from './org-unit-form-modal';
import { StaffAssignmentTable } from './staff-assignment-table';
import type { OrgUnitDto, OrgTreeNode } from './org-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';

// Build Ant Tree nodes from flat org unit list
function buildTree(units: OrgUnitDto[], parentId?: string | null): OrgTreeNode[] {
  return units
    .filter((u) => (u.parentId ?? undefined) === (parentId ?? undefined))
    .map((u) => ({
      key: u.id,
      title: `${u.name} (${u.code})`,
      isLeaf: u.childCount === 0,
      data: u,
      children: buildTree(units, u.id),
    }));
}

// OrgTreePage — hierarchical org unit tree with detail panel + staff table
export function OrgTreePage() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [editUnit, setEditUnit] = useState<OrgUnitDto | null>(null);
  const [selectedUnit, setSelectedUnit] = useState<OrgUnitDto | null>(null);

  const { data: units = [], isFetching } = useOrgUnits();

  const treeData = useMemo(() => buildTree(units, undefined), [units]);

  // Default-expand 1 level (root nodes) on first data arrival.
  // Controlled state so user expansion/collapse persists; ref guards single init
  // so a user collapse-all doesn't re-trigger auto-expand on next render.
  const [expandedKeys, setExpandedKeys] = useState<React.Key[]>([]);
  const didInitExpand = useRef(false);
  useEffect(() => {
    if (!didInitExpand.current && treeData.length > 0) {
      setExpandedKeys(treeData.map((n) => n.key));
      didInitExpand.current = true;
    }
  }, [treeData]);

  function handleSelect(_keys: React.Key[], info: Parameters<NonNullable<TreeProps<TreeDataNode>['onSelect']>>[1]) {
    const node = info.node as unknown as OrgTreeNode;
    setSelectedUnit(node.data ?? null);
  }

  function openCreate() {
    setEditUnit(null);
    setModalOpen(true);
  }

  function openEdit() {
    if (selectedUnit) {
      setEditUnit(selectedUnit);
      setModalOpen(true);
    }
  }

  return (
    <div>
      <AdminPageHeader
        title={t('page.admin.org.title')}
        stats={{ total: units.length, label: 'đơn vị' }}
        actions={
          <Space>
            <Button icon={<PlusOutlined />} onClick={openCreate} aria-label={t('common.add')}>{t('common.add')}</Button>
            <Button icon={<EditOutlined />} onClick={openEdit} disabled={!selectedUnit} aria-label={t('common.edit')}>{t('common.edit')}</Button>
          </Space>
        }
      />

      {/* Tree panel (xs: full width, lg: 8/24) + Detail panel (xs: full width, lg: 16/24) */}
      <Row gutter={[16, 16]} align="top">
        <Col xs={24} lg={8}>
          <Card
            size="small"
            title={t('page.admin.org.card.tree')}
            style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)' }}
          >
            {isFetching ? (
              <CenteredSpin tip={t('common.loading')} style={{ display: 'block', margin: '32px auto' }} />
            ) : (
              <Tree
                treeData={treeData}
                onSelect={handleSelect}
                expandedKeys={expandedKeys}
                onExpand={setExpandedKeys}
                showLine={{ showLeafIcon: false }}
                style={{ minHeight: 300 }}
              />
            )}
          </Card>
        </Col>

        {/* Right: detail panel + staff */}
        <Col xs={24} lg={16}>
          {selectedUnit ? (
            <Space direction="vertical" style={{ width: '100%' }} size={16}>
              <Card size="small" title={t('page.admin.org.card.detail')} style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)' }}>
                <Descriptions size="small" column={{ xs: 1, sm: 2 }}>
                  <Descriptions.Item label={t('page.admin.org.label.name')}>{selectedUnit.name}</Descriptions.Item>
                  <Descriptions.Item label={t('page.admin.org.label.code')}>
                    <Tag>{selectedUnit.code}</Tag>
                  </Descriptions.Item>
                  <Descriptions.Item label={t('page.admin.org.label.level')}>{selectedUnit.level}</Descriptions.Item>
                  <Descriptions.Item label={t('page.admin.org.label.childCount')}>{selectedUnit.childCount}</Descriptions.Item>
                  {selectedUnit.staffCount != null && (
                    <Descriptions.Item label={t('page.admin.org.label.staffCount')}>{selectedUnit.staffCount}</Descriptions.Item>
                  )}
                  {selectedUnit.description && (
                    <Descriptions.Item label={t('page.admin.org.label.description')} span={2}>
                      {selectedUnit.description}
                    </Descriptions.Item>
                  )}
                </Descriptions>
              </Card>

              <Card size="small" title={t('page.admin.org.card.staff')} style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)' }}>
                <StaffAssignmentTable unitId={selectedUnit.id} />
              </Card>
            </Space>
          ) : (
            <Card size="small" style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)' }}>
              <Typography.Text type="secondary">
                {t('page.admin.org.selectHint')}
              </Typography.Text>
            </Card>
          )}
        </Col>
      </Row>

      <OrgUnitFormModal
        open={modalOpen}
        editUnit={editUnit}
        allUnits={units}
        onClose={() => setModalOpen(false)}
      />
    </div>
  );
}
