import { Input, Select, DatePicker, Button, Space, Row, Col } from 'antd';
import { SearchOutlined, ClearOutlined } from '@ant-design/icons';
import type { Dayjs } from 'dayjs';
import { AUDIT_ACTIONS } from './audit-types';

const { RangePicker } = DatePicker;

export interface AuditLogFilterValues {
  search?: string;
  action?: string;
  moduleName?: string;
  resourceType?: string;
  from?: string;
  to?: string;
}

interface AuditLogFiltersProps {
  values: AuditLogFilterValues;
  onChange: (values: AuditLogFilterValues) => void;
  onReset: () => void;
}

// Common resource types across the platform
const RESOURCE_TYPES = ['Case', 'User', 'Role', 'Permission', 'Tenant', 'Setting'];
const MODULE_NAMES = ['Users', 'Cases', 'Audit', 'Auth', 'Admin', 'Settings'];

// AuditLogFilters — date range, action, module, resource type, keyword search
export function AuditLogFilters({ values, onChange, onReset }: AuditLogFiltersProps) {
  function handleDateRange(dates: [Dayjs | null, Dayjs | null] | null) {
    onChange({
      ...values,
      from: dates?.[0]?.toISOString(),
      to: dates?.[1]?.toISOString(),
    });
  }

  return (
    <Row gutter={[8, 8]} align="middle" style={{ marginBottom: 16 }}>
      <Col flex="220px">
        <RangePicker
          showTime
          format="DD/MM/YYYY HH:mm"
          onChange={handleDateRange}
          placeholder={['Từ ngày', 'Đến ngày']}
          style={{ width: '100%' }}
        />
      </Col>

      <Col flex="140px">
        <Select
          allowClear
          placeholder="Hành động"
          value={values.action}
          onChange={(v) => onChange({ ...values, action: v })}
          style={{ width: '100%' }}
          options={AUDIT_ACTIONS.map((a) => ({ value: a, label: a }))}
        />
      </Col>

      <Col flex="140px">
        <Select
          allowClear
          placeholder="Module"
          value={values.moduleName}
          onChange={(v) => onChange({ ...values, moduleName: v })}
          style={{ width: '100%' }}
          options={MODULE_NAMES.map((m) => ({ value: m, label: m }))}
        />
      </Col>

      <Col flex="140px">
        <Select
          allowClear
          placeholder="Loại tài nguyên"
          value={values.resourceType}
          onChange={(v) => onChange({ ...values, resourceType: v })}
          style={{ width: '100%' }}
          options={RESOURCE_TYPES.map((r) => ({ value: r, label: r }))}
        />
      </Col>

      <Col flex="auto">
        <Input
          prefix={<SearchOutlined />}
          placeholder="Tìm kiếm..."
          value={values.search}
          onChange={(e) => onChange({ ...values, search: e.target.value })}
          allowClear
        />
      </Col>

      <Col>
        <Space>
          <Button icon={<ClearOutlined />} onClick={onReset}>
            Đặt lại
          </Button>
        </Space>
      </Col>
    </Row>
  );
}
