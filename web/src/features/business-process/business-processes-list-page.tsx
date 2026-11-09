// business-processes-list-page.tsx — paged list with search + isActive filter
// Style-aligned with the rest of the admin shell (AdminPageHeader + AdminContentCard +
// AdminTableToolbar). The earlier raw `<div padding:24>` + raw `<Title>` layout pre-dated
// the shared design-system primitives and made BP visually divergent from 50+ sibling
// admin pages.
import { useState, useRef } from 'react';
import { Table, Button, Tag, Alert, Spin } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useTranslation } from 'react-i18next';
import { useNavigate } from '@tanstack/react-router';
import { usePermissions } from '@/features/auth/use-permissions';
import { AdminPageHeader } from '@/shared/components/admin-page-header';
import { AdminContentCard } from '@/shared/components/admin-content-card';
import { AdminTableToolbar } from '@/shared/components/admin-table-toolbar';
import { useBpList } from './business-process-admin-api';
import type { BpListItemDto } from './business-process-admin-api';

const PAGE_SIZE = 20;

export function BusinessProcessesListPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { hasPermission, isPermissionsLoading } = usePermissions();
  const canView = hasPermission('BUSINESSPROCESS.BP.VIEW');
  const canManage = hasPermission('BUSINESSPROCESS.BP.MANAGE');

  // All hooks must be declared before any early returns (Rules of Hooks)
  const [searchText, setSearchText] = useState<string>('');
  const [search, setSearch] = useState<string | undefined>(undefined);
  const [isActive, setIsActive] = useState<boolean | undefined>(undefined);
  const [page, setPage] = useState(1);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const { data, isLoading, isFetching } = useBpList({
    search,
    isActive,
    pageNumber: page,
    pageSize: PAGE_SIZE,
  });

  if (isPermissionsLoading) {
    return <Spin size="large" style={{ display: 'block', margin: '80px auto' }} />;
  }

  if (!canView) {
    return (
      <Alert
        type="error"
        message={t('common.forbidden', { defaultValue: 'Bạn không có quyền truy cập trang này.' })}
        style={{ margin: 24 }}
      />
    );
  }

  const items = data?.items ?? [];
  const total = data?.meta?.pagination?.totalCount ?? 0;

  // Local debounce so URL/query state only updates after typing pauses
  function handleSearchChange(val: string) {
    setSearchText(val);
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setSearch(val || undefined);
      setPage(1);
    }, 300);
  }

  function handleActiveChange(_key: string, val: string | undefined) {
    if (val === 'true') setIsActive(true);
    else if (val === 'false') setIsActive(false);
    else setIsActive(undefined);
    setPage(1);
  }

  const columns: ColumnsType<BpListItemDto> = [
    {
      title: t('page.businessProcessAdmin.fieldCode', { defaultValue: 'Mã' }),
      dataIndex: 'code',
      key: 'code',
      width: 160,
    },
    {
      title: t('page.businessProcessAdmin.fieldName', { defaultValue: 'Tên quy trình' }),
      dataIndex: 'name',
      key: 'name',
    },
    {
      title: t('page.businessProcessAdmin.fieldDescription', { defaultValue: 'Mô tả' }),
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
      render: (v: string | null | undefined) => v ?? '—',
    },
    {
      title: t('page.businessProcessAdmin.fieldStatus', { defaultValue: 'Trạng thái' }),
      dataIndex: 'isActive',
      key: 'isActive',
      width: 120,
      render: (v: boolean) => (
        <Tag color={v ? 'green' : 'default'}>
          {v ? t('common.active', { defaultValue: 'Hoạt động' }) : t('common.inactive', { defaultValue: 'Tắt' })}
        </Tag>
      ),
    },
    {
      title: t('page.businessProcessAdmin.fieldCreatedAt', { defaultValue: 'Tạo lúc' }),
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 180,
      render: (v: string) => new Date(v).toLocaleString('vi-VN'),
    },
  ];

  return (
    <>
      <AdminPageHeader
        title={t('page.businessProcessAdmin.listTitle', { defaultValue: 'Quản lý quy trình nghiệp vụ' })}
        stats={{ total }}
        actions={
          canManage && (
            <Button
              type="primary"
              onClick={() => void navigate({ to: '/admin/business-processes/new' })}
            >
              {t('page.businessProcessAdmin.createButton', { defaultValue: '+ Tạo mới' })}
            </Button>
          )
        }
      />
      <AdminContentCard noPadding>
        <AdminTableToolbar
          searchPlaceholder={t('common.search', { defaultValue: 'Tìm kiếm...' })}
          searchValue={searchText}
          onSearchChange={handleSearchChange}
          filters={[
            {
              key: 'isActive',
              placeholder: t('page.businessProcessAdmin.filterStatus', { defaultValue: 'Trạng thái' }),
              options: [
                { label: t('page.businessProcessAdmin.filterAll', { defaultValue: 'Tất cả' }), value: '' },
                { label: t('common.active', { defaultValue: 'Hoạt động' }), value: 'true' },
                { label: t('common.inactive', { defaultValue: 'Tắt' }), value: 'false' },
              ],
            },
          ]}
          filterValues={{
            isActive: isActive === true ? 'true' : isActive === false ? 'false' : '',
          }}
          onFilterChange={handleActiveChange}
        />
        <Table<BpListItemDto>
          rowKey="id"
          dataSource={items}
          columns={columns}
          loading={isLoading || isFetching}
          onRow={(record) => ({
            onClick: () => void navigate({ to: '/admin/business-processes/$id', params: { id: record.id } }),
            style: { cursor: 'pointer' },
          })}
          pagination={{
            current: page,
            pageSize: PAGE_SIZE,
            total,
            onChange: (p) => setPage(p),
            showTotal: (tot) => t('common.pagination.showTotal', { total: tot, defaultValue: `Tổng ${tot} bản ghi` }),
            showSizeChanger: false,
          }}
          locale={{ emptyText: t('common.noData', { defaultValue: 'Không có dữ liệu' }) }}
          size="middle"
        />
      </AdminContentCard>
    </>
  );
}
