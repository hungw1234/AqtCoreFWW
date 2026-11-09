import { useState } from 'react';
import { Tabs, Typography, Button, Space, Tag, Spin, Alert, Table, Input, Card } from 'antd';
import { ArrowLeftOutlined, PlayCircleOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useRuleSet,
  useDecisionTables,
  useTestRuleSet,
  type RuleDto,
  type DecisionTableDto,
  type RuleSetStatus,
} from './rules-api';
import { RuleFlowCanvas } from './designer';

const { Title, Text, Paragraph } = Typography;
const { TextArea } = Input;

const STATUS_COLOR: Record<RuleSetStatus, string> = {
  Draft: 'default',
  Active: 'green',
  Deprecated: 'red',
};

// ─── Rules tab ────────────────────────────────────────────────────────────────

// Rules are fetched as part of the RuleSet detail (BE GetRuleSet returns embedded list),
// so this tab renders what the parent already has — no standalone query.
function RulesTab({ rules, isLoading }: { rules: RuleDto[]; isLoading: boolean }) {
  const { t } = useTranslation();

  const columns: ColumnsType<RuleDto> = [
    { title: t('rules.col.ruleName'), dataIndex: 'name', key: 'name', ellipsis: true },
    { title: t('rules.col.priority'), dataIndex: 'priority', key: 'priority', width: 80 },
    { title: t('rules.col.expression'), dataIndex: 'expression', key: 'expression', ellipsis: true },
    { title: t('rules.col.failureAction'), dataIndex: 'failureAction', key: 'failureAction', width: 120 },
    {
      title: t('rules.col.enabled'),
      dataIndex: 'enabled',
      key: 'enabled',
      width: 80,
      render: (v: boolean) => (v ? '✓' : '—'),
    },
  ];

  return (
    <Table<RuleDto>
      rowKey="id"
      columns={columns}
      dataSource={rules}
      loading={isLoading}
      size="small"
      pagination={{ pageSize: 20 }}
      scroll={{ x: 700 }}
    />
  );
}

// ─── Decision tables tab ──────────────────────────────────────────────────────

function DecisionTablesTab({ ruleSetId }: { ruleSetId: string }) {
  const { t } = useTranslation();
  const { data: tables, isLoading } = useDecisionTables(ruleSetId);

  if (isLoading) return <Spin />;
  if (!tables?.length) return <Alert type="info" message={t('rules.noDecisionTables')} showIcon />;

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      {tables.map((dt: DecisionTableDto) => (
        <Card key={dt.id} title={dt.name} size="small">
          <Paragraph type="secondary" style={{ marginBottom: 8 }}>
            {t('rules.inputCols')}: {dt.inputColumns.join(', ')} | {t('rules.outputCols')}: {dt.outputColumns.join(', ')}
          </Paragraph>
          <Table
            rowKey={(_, i) => String(i)}
            dataSource={dt.rows}
            columns={[...dt.inputColumns, ...dt.outputColumns].map((col) => ({
              title: col,
              dataIndex: col,
              key: col,
              ellipsis: true,
              render: (v: unknown) => String(v ?? ''),
            }))}
            size="small"
            pagination={{ pageSize: 10 }}
            scroll={{ x: 500 }}
          />
        </Card>
      ))}
    </Space>
  );
}

// ─── Test runner tab ──────────────────────────────────────────────────────────

function TestRunnerTab({ ruleSetId }: { ruleSetId: string }) {
  const { t } = useTranslation();
  const [inputJson, setInputJson] = useState('{\n  \n}');
  const testMutation = useTestRuleSet();

  async function handleTest() {
    try {
      JSON.parse(inputJson);
    } catch {
      appMessage.error(t('rules.invalidJson'));
      return;
    }
    testMutation.mutate({ ruleSetId, inputJson }, {
      onSuccess: () => appMessage.success(t('rules.testSuccess', 'Đánh giá quy tắc thành công')),
      onError: () => appMessage.error(t('rules.testError', 'Đánh giá quy tắc thất bại')),
    });
  }

  const result = testMutation.data;

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Text strong>{t('rules.testInputLabel')}</Text>
      <TextArea
        rows={8}
        value={inputJson}
        onChange={(e) => setInputJson(e.target.value)}
        style={{ fontFamily: 'monospace' }}
        placeholder={'{\n  "key": "value"\n}'}
      />
      <Button
        type="primary"
        icon={<PlayCircleOutlined />}
        onClick={handleTest}
        loading={testMutation.isPending}
      >
        {t('rules.evaluateBtn')}
      </Button>

      {result && (
        <Card
          title={
            <Space>
              <Text strong>{t('rules.resultTitle')}</Text>
              <Tag color={result.matched ? 'green' : 'orange'}>
                {result.matched ? t('rules.matched') : t('rules.noMatch')}
              </Tag>
              {result.executionMs !== undefined && (
                <Text type="secondary">{result.executionMs}ms</Text>
              )}
            </Space>
          }
        >
          {result.output && (
            <Paragraph>
              <Text strong>{t('rules.outputLabel')}:</Text>
              <Text
                copyable
                code
                style={{ display: 'block', marginTop: 4 }}
              >
                {JSON.stringify(result.output, null, 2)}
              </Text>
            </Paragraph>
          )}
          {result.matchedRules && result.matchedRules.length > 0 && (
            <Paragraph>
              <Text strong>{t('rules.matchedRules')}:</Text>{' '}
              {result.matchedRules.join(', ')}
            </Paragraph>
          )}
        </Card>
      )}

      {testMutation.isError && (
        <Alert type="error" message={t('rules.testError')} showIcon />
      )}
    </Space>
  );
}

// ─── Main detail page ─────────────────────────────────────────────────────────

interface RuleSetDetailPageProps {
  ruleSetId: string;
}

// RuleSetDetailPage — tabs for Rules, Decision Tables, Visual Designer, Test Runner
export function RuleSetDetailPage({ ruleSetId }: RuleSetDetailPageProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: ruleSet, isLoading } = useRuleSet(ruleSetId);
  // Fetch decision tables at the page level so Designer tab shares the same cache entry.
  const { data: decisionTablesData } = useDecisionTables(ruleSetId);

  if (isLoading) return <Spin style={{ display: 'block', marginTop: 40 }} />;
  if (!ruleSet) return <Alert type="error" message={t('rules.notFound')} showIcon />;

  // Rules embedded in the RuleSet detail; decision tables come from the list endpoint.
  const rules: RuleDto[] = ruleSet.rules ?? [];
  const decisionTables: DecisionTableDto[] = decisionTablesData ?? [];

  const isReadOnly = ruleSet.status !== 'Draft';

  return (
    <div style={{ padding: '0 4px' }}>
      <Space style={{ marginBottom: 16 }}>
        <Button
          icon={<ArrowLeftOutlined />}
          onClick={() => navigate({ to: '/admin/rules', search: { page: 1, pageSize: 20 } })}
        >
          {t('rules.backToList')}
        </Button>
        <Title level={4} style={{ margin: 0 }}>{ruleSet.name}</Title>
        <Tag color={STATUS_COLOR[ruleSet.status]}>{ruleSet.status}</Tag>
        <Tag color="blue">v{ruleSet.version}</Tag>
      </Space>

      <Tabs
        defaultActiveKey="rules"
        items={[
          {
            key: 'rules',
            label: t('rules.tab.rules'),
            children: <RulesTab rules={rules} isLoading={false} />,
          },
          {
            key: 'decision-tables',
            label: t('rules.tab.decisionTables'),
            children: <DecisionTablesTab ruleSetId={ruleSetId} />,
          },
          {
            key: 'designer',
            label: t('rules.tab.designer'),
            children: (
              <RuleFlowCanvas
                ruleSetId={ruleSetId}
                rules={rules}
                decisionTables={decisionTables}
                isReadOnly={isReadOnly}
              />
            ),
          },
          {
            key: 'test',
            label: t('rules.tab.test'),
            children: <TestRunnerTab ruleSetId={ruleSetId} />,
          },
        ]}
      />
    </div>
  );
}
