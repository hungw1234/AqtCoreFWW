import { useState } from 'react';
import {
  Table,
  Button,
  Space,
  Tag,
  Modal,
  Form,
  Input,
  Popconfirm,
  Drawer,
  Descriptions,
  DatePicker,
  Select,
} from 'antd';
import { PlusOutlined, EyeOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { useDebouncedValue } from '@/core/hooks/use-debounced-value';
import {
  useContracts, useContract, useCreateContract, useActivateContract, useTerminateContract,
  usePartners,
  type ContractDto, type ContractStatus, type CreateContractDto,
} from './integration-api';
import { appMessage } from '@/core/api/notification-bridge';

const STATUS_COLOR: Record<ContractStatus, string> = {
  Draft: 'blue',
  Active: 'green',
  Expired: 'default',
  Terminated: 'red',
};

// ─── Detail drawer ────────────────────────────────────────────────────────────

function ContractDetailDrawer({ contractId, open, onClose }: {
  contractId: string; open: boolean; onClose: () => void;
}) {
  const { t } = useTranslation();
  const { data, isLoading } = useContract(contractId);
  const activateMutation = useActivateContract();
  const terminateMutation = useTerminateContract();

  return (
    <Drawer title={data?.title ?? t('integration.contracts.detailTitle')} open={open} onClose={onClose} width={540}>
      {!isLoading && data && (
        <Space direction="vertical" style={{ width: '100%' }} size={16}>
          <Descriptions column={1} bordered size="small">
            <Descriptions.Item label={t('integration.contracts.col.partnerId')}>{data.partnerId}</Descriptions.Item>
            <Descriptions.Item label={t('integration.contracts.col.description')}>{data.description ?? '—'}</Descriptions.Item>
            <Descriptions.Item label={t('integration.contracts.col.effectiveDate')}>
              {dayjs(data.effectiveDate).format('DD/MM/YYYY')}
            </Descriptions.Item>
            <Descriptions.Item label={t('integration.contracts.col.expiryDate')}>
              {data.expiryDate ? dayjs(data.expiryDate).format('DD/MM/YYYY') : '—'}
            </Descriptions.Item>
            <Descriptions.Item label={t('integration.contracts.col.status')}>
              <Tag color={STATUS_COLOR[data.status]}>{data.status}</Tag>
            </Descriptions.Item>
            {data.dataScopeJson && (
              <Descriptions.Item label={t('integration.contracts.col.dataScope')}>
                <pre style={{ margin: 0, fontSize: 12, maxHeight: 120, overflow: 'auto' }}>
                  {(() => { try { return JSON.stringify(JSON.parse(data.dataScopeJson), null, 2); } catch { return data.dataScopeJson; } })()}
                </pre>
              </Descriptions.Item>
            )}
            <Descriptions.Item label={t('integration.contracts.col.createdAt')}>
              {dayjs(data.createdAt).format('DD/MM/YYYY HH:mm')}
            </Descriptions.Item>
          </Descriptions>
          <Space>
            {data.status === 'Draft' && (
              <Popconfirm title={t('integration.contracts.confirmActivate')}
                onConfirm={() => activateMutation.mutate(data.id, {
                  onSuccess: () => appMessage.success(t('integration.contracts.activateSuccess', 'Kích hoạt hợp đồng thành công')),
                  onError: () => appMessage.error(t('integration.contracts.activateError', 'Kích hoạt hợp đồng thất bại')),
                })}
                okText={t('common.confirm')} cancelText={t('common.cancel')}>
                <Button type="primary" loading={activateMutation.isPending}>
                  {t('integration.contracts.activate')}
                </Button>
              </Popconfirm>
            )}
            {data.status === 'Active' && (
              <Popconfirm title={t('integration.contracts.confirmTerminate')}
                onConfirm={() => terminateMutation.mutate(data.id, {
                  onSuccess: () => appMessage.success(t('integration.contracts.terminateSuccess', 'Chấm dứt hợp đồng thành công')),
                  onError: () => appMessage.error(t('integration.contracts.terminateError', 'Chấm dứt hợp đồng thất bại')),
                })}
                okText={t('common.confirm')} cancelText={t('common.cancel')}>
                <Button danger loading={terminateMutation.isPending}>
                  {t('integration.contracts.terminate')}
                </Button>
              </Popconfirm>
            )}
          </Space>
        </Space>
      )}
    </Drawer>
  );
}

// ─── Create modal ─────────────────────────────────────────────────────────────

function CreateContractModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const [form] = Form.useForm<CreateContractDto & { effectiveDateObj?: object; expiryDateObj?: object }>();
  const createMutation = useCreateContract();
  const { data: partnersData } = usePartners({ pageSize: 100 });
  const partnerOptions = (partnersData?.items ?? []).map((p) => ({ value: p.id, label: p.name }));

  async function handleSave() {
    const values = await form.validateFields();
    try {
      await createMutation.mutateAsync({
        partnerPublicId: values.partnerPublicId,
        title: values.title,
        description: values.description,
        effectiveDate: values.effectiveDate,
        expiryDate: values.expiryDate,
        dataScopeJson: values.dataScopeJson,
      });
      appMessage.success(t('common.success'));
      form.resetFields();
      onClose();
    } catch {
      appMessage.error(t('common.error'));
    }
  }

  return (
    <Modal
      title={t('integration.contracts.createTitle')}
      open={open}
      onOk={handleSave}
      onCancel={() => { form.resetFields(); onClose(); }}
      confirmLoading={createMutation.isPending}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
        <Form.Item name="partnerPublicId" label={t('integration.contracts.col.partnerId')} rules={[{ required: true }]}>
          <Select options={partnerOptions} showSearch optionFilterProp="label"
            placeholder={t('integration.contracts.selectPartner')} />
        </Form.Item>
        <Form.Item name="title" label={t('integration.contracts.col.title')} rules={[{ required: true }]}>
          <Input />
        </Form.Item>
        <Form.Item name="description" label={t('integration.contracts.col.description')}>
          <Input.TextArea rows={2} />
        </Form.Item>
        <Form.Item name="effectiveDate" label={t('integration.contracts.col.effectiveDate')} rules={[{ required: true }]}>
          <DatePicker style={{ width: '100%' }}
            onChange={(_, str) => form.setFieldValue('effectiveDate', str)} />
        </Form.Item>
        <Form.Item name="expiryDate" label={t('integration.contracts.col.expiryDate')}>
          <DatePicker style={{ width: '100%' }}
            onChange={(_, str) => form.setFieldValue('expiryDate', str)} />
        </Form.Item>
        <Form.Item name="dataScopeJson" label={t('integration.contracts.col.dataScope')}>
          <Input.TextArea rows={3} placeholder='{"fields": ["name", "dob"]}' />
        </Form.Item>
      </Form>
    </Modal>
  );
}

// ─── List page ────────────────────────────────────────────────────────────────

// ContractListPage — integration contract management with partner filter and status actions
export function ContractListPage() {
  const { t } = useTranslation();
  const [createOpen, setCreateOpen] = useState(false);
  const [searchInput, setSearchInput] = useState('');
  const [detailId, setDetailId] = useState<string | null>(null);

  // Debounce search input by 300ms — prevents API call on every keystroke
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const { antPagination, toQueryParams } = useServerPagination(20, [debouncedSearch]);

  const { data, isFetching } = useContracts({
    ...toQueryParams(),
    search: debouncedSearch || undefined,
  });

  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  const columns: ColumnsType<ContractDto> = [
    { title: t('integration.contracts.col.title'), dataIndex: 'title', key: 'title', ellipsis: true },
    { title: t('integration.contracts.col.partnerId'), dataIndex: 'partnerId', key: 'partnerId', ellipsis: true, width: 220 },
    {
      title: t('integration.contracts.col.effectiveDate'), dataIndex: 'effectiveDate', key: 'effectiveDate', width: 130,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY'),
    },
    {
      title: t('integration.contracts.col.expiryDate'), dataIndex: 'expiryDate', key: 'expiryDate', width: 130,
      render: (v?: string) => (v ? dayjs(v).format('DD/MM/YYYY') : '—'),
    },
    {
      title: t('integration.contracts.col.status'), dataIndex: 'status', key: 'status', width: 110,
      render: (v: ContractStatus) => <Tag color={STATUS_COLOR[v]}>{v}</Tag>,
    },
    {
      title: '', key: 'actions', width: 80,
      render: (_, record) => (
        <Button size="small" icon={<EyeOutlined />} onClick={() => setDetailId(record.id)} />
      ),
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('integration.contracts.title')}
        stats={{ total, label: t('common.items') }}
        actions={
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreateOpen(true)}>
            {t('common.add')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchInput}
          onSearchChange={setSearchInput}
        />
        <Table<ContractDto>
          rowKey="id"
          columns={columns}
          dataSource={items}
          loading={isFetching}
          size="small"
          scroll={{ x: 800 }}
          pagination={{ ...antPagination, total }}
        />
      </AdminContentCard>

      <CreateContractModal open={createOpen} onClose={() => setCreateOpen(false)} />

      {detailId && (
        <ContractDetailDrawer
          contractId={detailId}
          open={Boolean(detailId)}
          onClose={() => setDetailId(null)}
        />
      )}
    </div>
  );
}
