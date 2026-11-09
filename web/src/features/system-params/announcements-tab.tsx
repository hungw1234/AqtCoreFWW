import { useState } from 'react';
import { Table, Button, Tag, Modal, Form, Input, DatePicker, Popconfirm } from 'antd';
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { useAnnouncements, useCreateAnnouncement, useDeleteAnnouncement } from './system-params-api';
import type { AnnouncementDto, AnnouncementStatus, CreateAnnouncementRequest } from './system-params-types';
import { appMessage } from '@/core/api/notification-bridge';

const { TextArea } = Input;
const { RangePicker } = DatePicker;

const STATUS_COLORS: Record<AnnouncementStatus, string> = {
  Draft: 'default',
  Active: 'green',
  Scheduled: 'blue',
  Expired: 'red',
};

// AnnouncementsTab — list + create + delete announcements
export function AnnouncementsTab() {
  const { t } = useTranslation();
  const [modalOpen, setModalOpen] = useState(false);
  const [form] = Form.useForm<CreateAnnouncementRequest & { dateRange?: [dayjs.Dayjs, dayjs.Dayjs] }>();

  const { data: announcements = [], isFetching } = useAnnouncements();
  const createMutation = useCreateAnnouncement();
  const deleteMutation = useDeleteAnnouncement();

  async function handleCreate() {
    try {
      const values = await form.validateFields();
      const body: CreateAnnouncementRequest = {
        title: values.title,
        content: values.content,
        startDate: values.dateRange?.[0]?.toISOString(),
        endDate: values.dateRange?.[1]?.toISOString(),
      };
      await createMutation.mutateAsync(body);
      appMessage.success(t('page.admin.systemParams.announcements.createSuccess'));
      setModalOpen(false);
      form.resetFields();
    } catch {
      // validation handled inline
    }
  }

  const COLUMNS: ColumnsType<AnnouncementDto> = [
    {
      title: t('page.admin.systemParams.announcements.colTitle'),
      dataIndex: 'title',
      key: 'title',
      ellipsis: true,
    },
    {
      title: t('page.admin.systemParams.announcements.colStatus'),
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (v: AnnouncementStatus) => <Tag color={STATUS_COLORS[v]}>{v}</Tag>,
    },
    {
      title: t('page.admin.systemParams.announcements.colStartDate'),
      dataIndex: 'startDate',
      key: 'startDate',
      width: 160,
      render: (v?: string) => v ? new Date(v).toLocaleDateString('vi-VN') : '—',
    },
    {
      title: t('page.admin.systemParams.announcements.colEndDate'),
      dataIndex: 'endDate',
      key: 'endDate',
      width: 160,
      render: (v?: string) => v ? new Date(v).toLocaleDateString('vi-VN') : '—',
    },
    {
      title: t('page.admin.systemParams.announcements.colActions'),
      key: 'actions',
      width: 80,
      render: (_, record) => (
        <Popconfirm
          title={t('page.admin.systemParams.announcements.deleteConfirm')}
          okText={t('common.delete')}
          cancelText={t('common.cancel')}
          okButtonProps={{ danger: true }}
          onConfirm={() => deleteMutation.mutate(record.id)}
        >
          <Button
            size="small"
            danger
            icon={<DeleteOutlined />}
            aria-label={t('common.delete')}
          />
        </Popconfirm>
      ),
    },
  ];

  return (
    <>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 8 }}>
        <Button type="primary" icon={<PlusOutlined />} onClick={() => setModalOpen(true)}>
          {t('page.admin.systemParams.announcements.btnCreate')}
        </Button>
      </div>

      <Table<AnnouncementDto>
        rowKey="id"
        columns={COLUMNS}
        dataSource={announcements}
        loading={isFetching}
        size="small"
        pagination={{ pageSize: 10 }}
        locale={{ emptyText: t('common.noData') }}
      />

      <Modal
        title={t('page.admin.systemParams.announcements.modalTitle')}
        open={modalOpen}
        onOk={handleCreate}
        onCancel={() => { setModalOpen(false); form.resetFields(); }}
        okText={t('common.add')}
        cancelText={t('common.cancel')}
        confirmLoading={createMutation.isPending}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            name="title"
            label={t('page.admin.systemParams.announcements.fieldTitle')}
            rules={[{ required: true }]}
          >
            <Input placeholder={t('page.admin.systemParams.announcements.titlePlaceholder')} />
          </Form.Item>
          <Form.Item
            name="content"
            label={t('page.admin.systemParams.announcements.fieldContent')}
            rules={[{ required: true }]}
          >
            <TextArea rows={4} placeholder={t('page.admin.systemParams.announcements.contentPlaceholder')} />
          </Form.Item>
          <Form.Item
            name="dateRange"
            label={t('page.admin.systemParams.announcements.fieldDateRange')}
          >
            <RangePicker style={{ width: '100%' }} format="DD/MM/YYYY" />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
