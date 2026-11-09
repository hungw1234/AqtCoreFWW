// Per-item result display for batch transition operations

import { Modal, Table, Tag, Statistic, Row, Col, Typography } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';
import type { BatchTransitionResult, BatchTransitionItemResult } from './workflow-types';

const { Text } = Typography;

interface Props {
  result: BatchTransitionResult | null;
  onClose: () => void;
}

export function WorkflowBatchResultDialog({ result, onClose }: Props) {
  const { t } = useTranslation();

  if (!result) return null;

  const columns: ColumnsType<BatchTransitionItemResult> = [
    {
      title: t('workflow.batch.resultStatus'),
      dataIndex: 'success',
      width: 90,
      render: (success: boolean) =>
        success ? (
          <Tag icon={<CheckCircleOutlined />} color="success">
            {t('common.success')}
          </Tag>
        ) : (
          <Tag icon={<CloseCircleOutlined />} color="error">
            {t('common.failed')}
          </Tag>
        ),
    },
    {
      title: t('workflow.batch.resultInstanceId'),
      dataIndex: 'instanceId',
      ellipsis: true,
      render: (v: string) => <Text code>{v}</Text>,
    },
    {
      title: t('workflow.batch.resultError'),
      dataIndex: 'error',
      render: (error: string | null) =>
        error ? <Text type="danger">{error}</Text> : <Text type="secondary">—</Text>,
    },
  ];

  return (
    <Modal
      title={t('workflow.batch.resultTitle')}
      open={!!result}
      onCancel={onClose}
      onOk={onClose}
      okText={t('common.close')}
      cancelButtonProps={{ style: { display: 'none' } }}
      width={720}
      destroyOnHidden
    >
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={8}>
          <Statistic title={t('workflow.batch.totalCount')} value={result.totalCount} />
        </Col>
        <Col span={8}>
          <Statistic
            title={t('workflow.batch.successCount')}
            value={result.successCount}
            valueStyle={{ color: '#3f8600' }}
          />
        </Col>
        <Col span={8}>
          <Statistic
            title={t('workflow.batch.failureCount')}
            value={result.failureCount}
            valueStyle={result.failureCount > 0 ? { color: '#cf1322' } : undefined}
          />
        </Col>
      </Row>

      <Table<BatchTransitionItemResult>
        rowKey="instanceId"
        columns={columns}
        dataSource={result.items}
        size="small"
        pagination={false}
        scroll={{ y: 360 }}
      />
    </Modal>
  );
}
