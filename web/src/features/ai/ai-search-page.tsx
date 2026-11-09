import { useState } from 'react';
import { Table, Typography, Alert, Input } from 'antd';
import { CenteredSpin } from '@/shared/components/centered-spin';
import { SendOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useNlqQuery } from './ai-api';
import type { ExecuteQueryResponse } from './ai-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { DeferredFeatureAlert } from '@/shared/components/deferred-feature-alert';
import { appMessage } from '@/core/api/notification-bridge';

const { Text, Paragraph } = Typography;
const { Search } = Input;

// AiSearchPage — NLQ (natural language query) via AI chat endpoint
export function AiSearchPage() {
  const { t } = useTranslation();
  const [question, setQuestion] = useState('');
  const [result, setResult] = useState<ExecuteQueryResponse | null>(null);
  const { mutate: nlqQuery, isPending } = useNlqQuery();

  function handleSubmit() {
    if (!question.trim()) return;
    nlqQuery(question, {
      onSuccess: (data) => setResult(data),
      onError: () => {
        setResult(null);
        appMessage.error(t('page.ai.queryError', 'Truy vấn thất bại'));
      },
    });
  }

  // Build dynamic columns from result
  const resultColumns: ColumnsType<Record<string, unknown>> =
    result?.columns?.map((col) => ({
      title: col,
      dataIndex: col,
      key: col,
      ellipsis: true,
      render: (v: unknown) =>
        v instanceof Date ? dayjs(v as Date).format('DD/MM/YYYY') : String(v ?? ''),
    })) ?? [];

  return (
    <>
      <AdminPageHeader title={t('page.ai.title')} />

      <DeferredFeatureAlert feature="AI Search" />

      {/* NLQ input */}
      <AdminContentCard>
        <Paragraph type="secondary" style={{ marginBottom: 8 }}>
          {t('page.ai.nlqHint')}
        </Paragraph>
        <Search
          placeholder={t('page.ai.nlqPlaceholder')}
          value={question}
          onChange={(e: React.ChangeEvent<HTMLInputElement>) => setQuestion(e.target.value)}
          onSearch={handleSubmit}
          enterButton={<><SendOutlined /> {t('page.ai.btnSend')}</>}
          loading={isPending}
          size="large"
          style={{ maxWidth: 800 }}
        />
      </AdminContentCard>

      {/* Query results */}
      {isPending && <CenteredSpin tip={t('common.loading')} style={{ display: 'block', marginBottom: 16 }} />}

      {result && !isPending && (
        <AdminContentCard noPadding>
          <div style={{ padding: '12px 16px', borderBottom: '1px solid var(--gov-border)' }}>
            <span>{t('page.ai.resultTitle')}: <Text type="secondary">{question}</Text></span>
            <Text type="secondary" style={{ float: 'right' }}>
              {result.rowCount} {t('page.ai.rows')}{result.executionMs ? ` · ${result.executionMs}ms` : ''}
            </Text>
          </div>
          {(!result.rows || result.rows.length === 0) ? (
            <div style={{ padding: 16 }}>
              <Alert type="info" message={t('common.noData')} showIcon />
            </div>
          ) : (
            <Table
              rowKey={(_, i) => String(i)}
              columns={resultColumns}
              dataSource={result.rows}
              size="small"
              pagination={{ pageSize: 20 }}
              scroll={{ x: 700 }}
            />
          )}
        </AdminContentCard>
      )}
    </>
  );
}
