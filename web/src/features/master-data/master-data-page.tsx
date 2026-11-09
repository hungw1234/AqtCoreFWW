import { useState } from 'react';
import { Select, Table, Space, Card, Tag, Button, Modal, Form, Input, InputNumber, Popconfirm } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { appMessage } from '@/core/api/notification-bridge';
import {
  useProvinces, useDistricts, useWards,
  useCreateProvince, useUpdateProvince, useDeleteProvince,
  useCreateDistrict, useUpdateDistrict, useDeleteDistrict,
  useCreateWard, useUpdateWard, useDeleteWard,
  type UpsertProvincePayload, type UpsertDistrictPayload, type UpsertWardPayload,
} from './master-data-api';
import type { ProvinceDto, DistrictDto, WardDto } from './master-data-types';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { foldDiacritics } from '@/shared/utils';

// ── Types ─────────────────────────────────────────────────────────────────────

type AdminLevel = 'province' | 'district' | 'ward';
type ModalMode = 'create' | 'edit';

interface ModalState {
  open: boolean;
  mode: ModalMode;
  level: AdminLevel;
  record?: ProvinceDto | DistrictDto | WardDto;
}

// MasterDataPage — cascading Province → District → Ward viewer with admin CRUD
export function MasterDataPage() {
  const { t } = useTranslation();
  const [selectedProvince, setSelectedProvince] = useState<string | null>(null);
  const [selectedDistrict, setSelectedDistrict] = useState<string | null>(null);
  const [searchText, setSearchText] = useState('');
  const [modal, setModal] = useState<ModalState>({ open: false, mode: 'create', level: 'province' });
  const [form] = Form.useForm();

  const { data: provinces = [], isFetching: loadingProvinces } = useProvinces();
  const { data: districts = [], isFetching: loadingDistricts } = useDistricts(selectedProvince);
  const { data: wards = [], isFetching: loadingWards } = useWards(selectedDistrict);

  const createProvince = useCreateProvince();
  const updateProvince = useUpdateProvince();
  const deleteProvince = useDeleteProvince();
  const createDistrict = useCreateDistrict();
  const updateDistrict = useUpdateDistrict();
  const deleteDistrict = useDeleteDistrict();
  const createWard = useCreateWard();
  const updateWard = useUpdateWard();
  const deleteWard = useDeleteWard();

  function handleProvinceChange(id: string) {
    setSelectedProvince(id);
    setSelectedDistrict(null);
    setSearchText('');
  }

  function openCreate(level: AdminLevel) {
    form.resetFields();
    setModal({ open: true, mode: 'create', level });
  }

  function openEdit(level: AdminLevel, record: ProvinceDto | DistrictDto | WardDto) {
    form.setFieldsValue(record);
    setModal({ open: true, mode: 'edit', level, record });
  }

  function closeModal() {
    setModal((s) => ({ ...s, open: false }));
    form.resetFields();
  }

  async function handleSubmit() {
    const values = await form.validateFields();
    const isEdit = modal.mode === 'edit';
    const t_ok = () => { appMessage.success(isEdit ? t('page.admin.masterData.updateSuccess') : t('page.admin.masterData.createSuccess')); closeModal(); };
    const t_err = () => appMessage.error(t('page.admin.masterData.saveFail'));

    if (modal.level === 'province') {
      const payload = values as UpsertProvincePayload;
      if (isEdit) updateProvince.mutate(payload, { onSuccess: t_ok, onError: t_err });
      else createProvince.mutate(payload, { onSuccess: t_ok, onError: t_err });
    } else if (modal.level === 'district') {
      const payload = { ...values, provinceCode: selectedProvince ?? '' } as UpsertDistrictPayload;
      if (isEdit) updateDistrict.mutate(payload, { onSuccess: t_ok, onError: t_err });
      else createDistrict.mutate(payload, { onSuccess: t_ok, onError: t_err });
    } else {
      const payload = { ...values, districtCode: selectedDistrict ?? '' } as UpsertWardPayload;
      if (isEdit) updateWard.mutate(payload, { onSuccess: t_ok, onError: t_err });
      else createWard.mutate(payload, { onSuccess: t_ok, onError: t_err });
    }
  }

  function handleDelete(level: AdminLevel, code: string) {
    const ok = () => appMessage.success(t('page.admin.masterData.deleteSuccess'));
    const err = () => appMessage.error(t('page.admin.masterData.saveFail'));
    if (level === 'province') deleteProvince.mutate(code, { onSuccess: ok, onError: err });
    else if (level === 'district') deleteDistrict.mutate(code, { onSuccess: ok, onError: err });
    else deleteWard.mutate(code, { onSuccess: ok, onError: err });
  }

  const actionCol = (level: AdminLevel): ColumnsType<ProvinceDto | DistrictDto | WardDto>[number] => ({
    title: t('page.admin.masterData.col.actions'),
    key: 'actions',
    width: 90,
    render: (_, record) => (
      <Space size="small">
        <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(level, record)} />
        <Popconfirm
          title={t('page.admin.masterData.deleteConfirm.title')}
          description={t('page.admin.masterData.deleteConfirm.description')}
          onConfirm={() => handleDelete(level, (record as { code: string }).code)}
          okText={t('common.delete')}
          cancelText={t('common.cancel')}
        >
          <Button size="small" danger icon={<DeleteOutlined />} />
        </Popconfirm>
      </Space>
    ),
  });

  const provinceColumns: ColumnsType<ProvinceDto> = [
    { title: t('page.admin.masterData.col.code'), dataIndex: 'code', key: 'code', width: 80 },
    { title: t('page.admin.masterData.col.provinceName'), dataIndex: 'name', key: 'name' },
    { title: t('page.admin.masterData.col.districtCount'), dataIndex: 'districtCount', key: 'districtCount', width: 100, render: (v?: number) => v != null ? <Tag>{v}</Tag> : '—' },
    actionCol('province') as ColumnsType<ProvinceDto>[number],
  ];

  const districtColumns: ColumnsType<DistrictDto> = [
    { title: t('page.admin.masterData.col.code'), dataIndex: 'code', key: 'code', width: 80 },
    { title: t('page.admin.masterData.col.districtName'), dataIndex: 'name', key: 'name' },
    { title: t('page.admin.masterData.col.wardCount'), dataIndex: 'wardCount', key: 'wardCount', width: 100, render: (v?: number) => v != null ? <Tag>{v}</Tag> : '—' },
    actionCol('district') as ColumnsType<DistrictDto>[number],
  ];

  const wardColumns: ColumnsType<WardDto> = [
    { title: t('page.admin.masterData.col.code'), dataIndex: 'code', key: 'code', width: 80 },
    { title: t('page.admin.masterData.col.wardName'), dataIndex: 'name', key: 'name' },
    actionCol('ward') as ColumnsType<WardDto>[number],
  ];

  const saving = createProvince.isPending || updateProvince.isPending ||
                 createDistrict.isPending || updateDistrict.isPending ||
                 createWard.isPending || updateWard.isPending;

  return (
    <div>
      <AdminPageHeader
        title={t('page.admin.masterData.title')}
        actions={
          <Space wrap>
            <Select
              placeholder={t('page.admin.masterData.selectProvince')}
              style={{ width: 220 }}
              loading={loadingProvinces}
              allowClear
              showSearch
              filterOption={(input, opt) => foldDiacritics(String(opt?.label ?? '')).includes(foldDiacritics(input))}
              options={provinces.map((p) => ({ value: p.id, label: p.name }))}
              onChange={handleProvinceChange}
              onClear={() => { setSelectedProvince(null); setSelectedDistrict(null); }}
            />
            {selectedProvince && (
              <Select
                placeholder={t('page.admin.masterData.selectDistrict')}
                style={{ width: 220 }}
                loading={loadingDistricts}
                allowClear
                showSearch
                filterOption={(input, opt) => foldDiacritics(String(opt?.label ?? '')).includes(foldDiacritics(input))}
                options={districts.map((d) => ({ value: d.id, label: d.name }))}
                onChange={(id) => setSelectedDistrict(id)}
                onClear={() => setSelectedDistrict(null)}
              />
            )}
          </Space>
        }
      />

      {/* Detail tables with Create button for admin */}
      <Space direction="vertical" style={{ width: '100%' }} size={16}>
        {!selectedProvince && (
          <Card
            size="small"
            title={t('page.admin.masterData.card.provinces')}
            extra={<Button size="small" icon={<PlusOutlined />} onClick={() => openCreate('province')}>{t('page.admin.masterData.create')}</Button>}
            style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)' }}
          >
            <AdminTableToolbar
              searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
              searchValue={searchText}
              onSearchChange={setSearchText}
            />
            <Table<ProvinceDto>
              rowKey="id"
              columns={provinceColumns}
              dataSource={provinces.filter(item =>
                !searchText || Object.values(item).some(v =>
                  foldDiacritics(String(v ?? '')).includes(foldDiacritics(searchText))
                )
              )}
              loading={loadingProvinces}
              size="small"
              pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
              onRow={(r) => ({ onClick: () => handleProvinceChange(r.id), style: { cursor: 'pointer' } })}
            />
          </Card>
        )}

        {selectedProvince && !selectedDistrict && (
          <Card
            size="small"
            title={t('page.admin.masterData.card.districts')}
            extra={<Button size="small" icon={<PlusOutlined />} onClick={() => openCreate('district')}>{t('page.admin.masterData.create')}</Button>}
            style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)' }}
          >
            <AdminTableToolbar
              searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
              searchValue={searchText}
              onSearchChange={setSearchText}
            />
            <Table<DistrictDto>
              rowKey="id"
              columns={districtColumns}
              dataSource={districts.filter(item =>
                !searchText || Object.values(item).some(v =>
                  foldDiacritics(String(v ?? '')).includes(foldDiacritics(searchText))
                )
              )}
              loading={loadingDistricts}
              size="small"
              pagination={{ pageSize: 20, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
              onRow={(r) => ({ onClick: () => setSelectedDistrict(r.id), style: { cursor: 'pointer' } })}
            />
          </Card>
        )}

        {selectedDistrict && (
          <Card
            size="small"
            title={t('page.admin.masterData.card.wards')}
            extra={<Button size="small" icon={<PlusOutlined />} onClick={() => openCreate('ward')}>{t('page.admin.masterData.create')}</Button>}
            style={{ borderRadius: 12, boxShadow: 'var(--elevation-1)' }}
          >
            <AdminTableToolbar
              searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
              searchValue={searchText}
              onSearchChange={setSearchText}
            />
            <Table<WardDto>
              rowKey="id"
              columns={wardColumns}
              dataSource={wards.filter(item =>
                !searchText || Object.values(item).some(v =>
                  foldDiacritics(String(v ?? '')).includes(foldDiacritics(searchText))
                )
              )}
              loading={loadingWards}
              size="small"
              pagination={{ pageSize: 30, showSizeChanger: true, showTotal: (t: number) => `Tổng ${t}` }}
            />
          </Card>
        )}
      </Space>

      {/* Create / Edit modal */}
      <Modal
        open={modal.open}
        title={modal.mode === 'edit' ? t('page.admin.masterData.edit') : t('page.admin.masterData.create')}
        onCancel={closeModal}
        onOk={handleSubmit}
        okText={modal.mode === 'edit' ? t('common.save') : t('common.add')}
        cancelText={t('common.cancel')}
        confirmLoading={saving}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="code"
            label={t('page.admin.masterData.col.code')}
            validateFirst
            rules={[
              { required: true },
              // FE-only guard — MasterDataAdminController.cs uses record DTOs without FluentValidation
              { max: 100, message: t('common.validation.maxLength', { max: 100 }) },
              { pattern: /^[A-Z0-9_]+$/, message: t('common.validation.pattern.upperAlphaNum') },
            ]}
          >
            <Input disabled={modal.mode === 'edit'} />
          </Form.Item>
          <Form.Item
            name="nameVi"
            label="Tên (Vi)"
            validateFirst
            rules={[
              { required: true },
              // FE-only guard — MasterDataAdminController.cs uses record DTOs without FluentValidation
              { max: 200, message: t('common.validation.maxLength', { max: 200 }) },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="nameEn"
            label="Name (En)"
            validateFirst
            rules={[
              { required: true },
              // FE-only guard — MasterDataAdminController.cs uses record DTOs without FluentValidation
              { max: 200, message: t('common.validation.maxLength', { max: 200 }) },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item name="sortOrder" label="Sort Order">
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
