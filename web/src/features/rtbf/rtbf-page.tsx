// RTBF admin page — list, create, process, reject Right-to-Be-Forgotten requests

import { useState } from 'react';
import {
  Table,
  Button,
  Tag,
  Space,
  Popconfirm,
  Modal,
  Form,
  Input,
  Tooltip,
} from 'antd';
import { PlusOutlined, CheckCircleOutlined, CloseCircleOutlined, ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import {
  useRtbfRequests,
  useCreateRtbf,
  useProcessRtbf,
  useRejectRtbf,
  type RtbfRequestDto,
  type CreateRtbfRequest,
} from './rtbf-api';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { useServerPagination } from '@/core/hooks/use-server-pagination';
import { useDebouncedValue } from '@/core/hooks/use-debounced-value';
import { appMessage } from '@/core/api/notification-bridge';

// Status tag color map
const STATUS_COLORS: Record<string, string> = {
  Pending: 'orange',
  Processing: 'blue',
  Completed: 'green',
  Rejected: 'red',
  PartiallyCompleted: 'gold',
};

export function RtbfPage() {
  const { t } = useTranslation();
  const [searchInput, setSearchInput] = useState('');
  const [createOpen, setCreateOpen] = useState(false);

  // Debounce search input by 300ms — prevents API call on every keystroke
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  // useRtbfRequests uses page/pageSize (not pageNumber) — extract directly from hook
  const { page, pageSize, antPagination } = useServerPagination(20, [debouncedSearch]);

  const { data, isLoading } = useRtbfRequests({
    page,
    pageSize,
    search: debouncedSearch || undefined,
  });
  const items = data?.items ?? [];
  const total = data?.totalCount ?? 0;

  const createMutation = useCreateRtbf();
  const processMutation = useProcessRtbf();
  const rejectMutation = useRejectRtbf();
  const [createForm] = Form.useForm<CreateRtbfRequest>();

  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectingId, setRejectingId] = useState<string | null>(null);
  const [rejectForm] = Form.useForm<{ reason: string }>();

  const handleCreate = async () => {
    const values = await createForm.validateFields();
    try {
      await createMutation.mutateAsync(values);
      appMessage.success(t('rtbf.createSuccess', 'Đã tạo yêu cầu xoá dữ liệu'));
      createForm.resetFields();
      setCreateOpen(false);
    } catch {
      appMessage.error(t('common.error', 'Có lỗi xảy ra'));
    }
  };

  const handleProcess = async (id: string) => {
    try {
      await processMutation.mutateAsync({ id });
      appMessage.success(t('rtbf.processSuccess', 'Đang xử lý yêu cầu'));
    } catch {
      appMessage.error(t('common.error', 'Có lỗi xảy ra'));
    }
  };

  const handleRejectOpen = (id: string) => {
    setRejectingId(id);
    rejectForm.resetFields();
    setRejectOpen(true);
  };

  const handleRejectConfirm = async () => {
    if (!rejectingId) return;
    const { reason } = await rejectForm.validateFields();
    try {
      await rejectMutation.mutateAsync({ id: rejectingId, reason });
      appMessage.success(t('rtbf.rejectSuccess', 'Đã từ chối yêu cầu'));
      setRejectOpen(false);
      setRejectingId(null);
    } catch {
      appMessage.error(t('common.error', 'Có lỗi xảy ra'));
    }
  };

  const columns: ColumnsType<RtbfRequestDto> = [
    {
      title: t('rtbf.col.dataSubject', 'Chủ thể dữ liệu'),
      dataIndex: 'dataSubjectEmail',
      key: 'dataSubjectEmail',
      ellipsis: true,
      render: (v?: string, record?: RtbfRequestDto) => v ?? record?.dataSubjectId ?? '—',
    },
    {
      title: t('rtbf.col.citizenNationalId', 'CCCD/Hộ chiếu'),
      dataIndex: 'citizenNationalId',
      key: 'citizenNationalId',
      width: 140,
      render: (v?: string) => v ?? '—',
    },
    {
      title: t('rtbf.col.status', 'Trạng thái'),
      dataIndex: 'status',
      key: 'status',
      width: 160,
      render: (v: string) => <Tag color={STATUS_COLORS[v] ?? 'default'}>{v}</Tag>,
    },
    {
      title: t('rtbf.col.requestedAt', 'Ngày yêu cầu'),
      dataIndex: 'requestedAt',
      key: 'requestedAt',
      width: 150,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
    {
      title: t('rtbf.col.dueBy', 'Hạn xử lý'),
      dataIndex: 'dueBy',
      key: 'dueBy',
      width: 150,
      render: (v: string) => dayjs(v).format('DD/MM/YYYY HH:mm'),
    },
    {
      title: '',
      key: 'actions',
      width: 100,
      render: (_: unknown, record: RtbfRequestDto) => {
        const isPending = record.status === 'Pending';
        const isPartial = record.status === 'PartiallyCompleted';
        return (
          <Space size="small">
            {(isPending || isPartial) && (
              <Popconfirm
                title={t('rtbf.processConfirm', 'Xác nhận xử lý xoá dữ liệu?')}
                onConfirm={() => handleProcess(record.id)}
                okText={t('common.confirm', 'Xác nhận')}
                cancelText={t('common.cancel', 'Hủy')}
              >
                <Tooltip title={isPartial ? t('rtbf.retry', 'Thử lại') : t('rtbf.process', 'Xử lý')}>
                  <Button size="small" type="primary" icon={isPartial ? <ReloadOutlined /> : <CheckCircleOutlined />} loading={processMutation.isPending} />
                </Tooltip>
              </Popconfirm>
            )}
            {isPending && (
              <Tooltip title={t('rtbf.reject', 'Từ chối')}>
                <Button size="small" danger icon={<CloseCircleOutlined />} onClick={() => handleRejectOpen(record.id)} />
              </Tooltip>
            )}
          </Space>
        );
      },
    },
  ];

  return (
    <div>
      <AdminPageHeader
        title={t('rtbf.title', 'Yêu cầu xoá dữ liệu (RTBF)')}
        stats={{ total, label: 'yêu cầu' }}
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => { createForm.resetFields(); setCreateOpen(true); }}
          >
            {t('common.add', 'Thêm mới')}
          </Button>
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchInput}
          onSearchChange={setSearchInput}
        />
        <Table<RtbfRequestDto>
          rowKey="id"
          columns={columns}
          dataSource={items}
          loading={isLoading}
          size="small"
          scroll={{ x: 900 }}

          pagination={{ ...antPagination, total }}
        />
      </AdminContentCard>

      {/* Create modal */}
      <Modal
        title={t('rtbf.createTitle', 'Tạo yêu cầu xoá dữ liệu')}
        open={createOpen}
        onOk={handleCreate}
        onCancel={() => setCreateOpen(false)}
        confirmLoading={createMutation.isPending}
        okText={t('common.save', 'Lưu')}
        cancelText={t('common.cancel', 'Hủy')}
        destroyOnHidden
      >
        <Form form={createForm} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="subjectEmail"
            label={t('rtbf.col.subjectEmail', 'Email chủ thể dữ liệu')}
            rules={[
              { required: true, message: t('rtbf.subjectEmailRequired', 'Vui lòng nhập email') },
              { type: 'email', message: t('rtbf.subjectEmailInvalid', 'Email không hợp lệ') },
            ]}
          >
            <Input placeholder="example@domain.com" />
          </Form.Item>
          <Form.Item name="reason" label={t('rtbf.col.reason', 'Lý do yêu cầu (tuỳ chọn)')}>
            <Input.TextArea rows={3} placeholder={t('rtbf.reasonPlaceholder', 'Mô tả lý do yêu cầu xoá dữ liệu...')} />
          </Form.Item>
        </Form>
      </Modal>

      {/* Reject modal */}
      <Modal
        title={t('rtbf.rejectTitle', 'Từ chối yêu cầu')}
        open={rejectOpen}
        onOk={handleRejectConfirm}
        onCancel={() => { setRejectOpen(false); setRejectingId(null); }}
        confirmLoading={rejectMutation.isPending}
        okText={t('rtbf.reject', 'Từ chối')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel', 'Hủy')}
        destroyOnHidden
      >
        <Form form={rejectForm} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item
            name="reason"
            label={t('rtbf.rejectionReason', 'Lý do từ chối')}
            rules={[{ required: true, message: t('rtbf.reasonRequired', 'Vui lòng nhập lý do') }]}
          >
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
