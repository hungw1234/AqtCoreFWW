import { useState } from 'react';
import { Table, Input, Button, Space, Tag, Tooltip } from 'antd';
import { SaveOutlined, EditOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useSystemParams, useUpdateSystemParam } from './system-params-api';
import type { SystemParamDto } from './system-params-types';
import { appMessage } from '@/core/api/notification-bridge';

// ParamsTable — inline-editable key/value system parameters table
export function ParamsTable() {
  const { t } = useTranslation();
  const { data: params = [], isFetching } = useSystemParams();
  const updateMutation = useUpdateSystemParam();

  // Track which row is being edited and its current draft value
  const [editingKey, setEditingKey] = useState<string | null>(null);
  const [draftValue, setDraftValue] = useState('');

  function startEdit(param: SystemParamDto) {
    setEditingKey(param.key);
    setDraftValue(param.value);
  }

  async function saveEdit(param: SystemParamDto) {
    try {
      await updateMutation.mutateAsync({
        key: param.key,
        body: { value: draftValue, description: param.description },
      });
      appMessage.success(t('page.admin.systemParams.params.saveSuccess', { key: param.key }));
      setEditingKey(null);
    } catch {
      appMessage.error(t('page.admin.systemParams.params.saveFail'));
    }
  }

  const COLUMNS: ColumnsType<SystemParamDto> = [
    {
      title: t('page.admin.systemParams.params.colKey'),
      dataIndex: 'key',
      key: 'key',
      width: 220,
      render: (v: string) => <code style={{ fontSize: 12 }}>{v}</code>,
    },
    {
      title: t('page.admin.systemParams.params.colValue'),
      dataIndex: 'value',
      key: 'value',
      render: (v: string, record) => {
        if (editingKey === record.key) {
          return (
            <Input
              value={draftValue}
              onChange={(e) => setDraftValue(e.target.value)}
              onPressEnter={() => saveEdit(record)}
              autoFocus
              style={{ maxWidth: 320 }}
            />
          );
        }
        return record.isEncrypted ? (
          <Tag color="orange">••••••••</Tag>
        ) : (
          <span style={{ fontFamily: 'monospace', fontSize: 12 }}>{v}</span>
        );
      },
    },
    {
      title: t('page.admin.systemParams.params.colDescription'),
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
    },
    {
      title: t('page.admin.systemParams.params.colCategory'),
      dataIndex: 'category',
      key: 'category',
      width: 120,
      render: (v?: string) => v ? <Tag>{v}</Tag> : null,
    },
    {
      title: t('page.admin.systemParams.params.colActions'),
      key: 'actions',
      width: 100,
      render: (_, record) =>
        editingKey === record.key ? (
          <Space>
            <Button
              size="small"
              type="primary"
              icon={<SaveOutlined />}
              loading={updateMutation.isPending}
              onClick={() => saveEdit(record)}
              aria-label={t('common.save')}
            >
              {t('common.save')}
            </Button>
            <Button size="small" onClick={() => setEditingKey(null)}>
              {t('common.cancel')}
            </Button>
          </Space>
        ) : (
          <Tooltip
            title={
              record.isEncrypted
                ? t('page.admin.systemParams.params.tooltipEncrypted')
                : t('page.admin.systemParams.params.tooltipEdit')
            }
          >
            <Button
              size="small"
              icon={<EditOutlined />}
              aria-label={t('page.admin.systemParams.params.tooltipEdit')}
              disabled={record.isEncrypted}
              onClick={() => startEdit(record)}
            />
          </Tooltip>
        ),
    },
  ];

  return (
    <Table<SystemParamDto>
      rowKey="key"
      columns={COLUMNS}
      dataSource={params}
      loading={isFetching}
      size="small"
      scroll={{ x: 700 }}
      pagination={{ pageSize: 30, showSizeChanger: false }}
      locale={{ emptyText: t('common.noData') }}
    />
  );
}
