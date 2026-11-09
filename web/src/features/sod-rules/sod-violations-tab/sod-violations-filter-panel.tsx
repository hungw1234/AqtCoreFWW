import { Form, Select, DatePicker, Button, Space } from 'antd';
import { SearchOutlined, ClearOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import type { SodViolationsFilters } from './sod-violations-types';
import { useSodRules } from '../sod-rules-api';

const { RangePicker } = DatePicker;

interface Props {
  filters: SodViolationsFilters;
  onFiltersChange: (filters: SodViolationsFilters) => void;
  loading?: boolean;
}

/** Filter panel for SoD violations — user/role/rule selects + date range picker */
export function SodViolationsFilterPanel({ filters, onFiltersChange, loading }: Props) {
  const { t } = useTranslation();
  const [form] = Form.useForm();
  const { data: rulesData } = useSodRules();

  const rules = rulesData ?? [];

  const handleSearch = () => {
    const values = form.getFieldsValue();
    const newFilters: SodViolationsFilters = {
      userId: values.userId || undefined,
      roleId: values.roleId || undefined,
      ruleId: values.ruleId || undefined,
      from: values.dateRange?.[0]?.toISOString() ?? undefined,
      to: values.dateRange?.[1]?.toISOString() ?? undefined,
    };
    onFiltersChange(newFilters);
  };

  const handleClear = () => {
    form.resetFields();
    onFiltersChange({});
  };

  return (
    <Form
      form={form}
      layout="inline"
      style={{ marginBottom: 16, flexWrap: 'wrap', gap: 8 }}
      initialValues={{
        userId: filters.userId,
        roleId: filters.roleId,
        ruleId: filters.ruleId,
      }}
    >
      <Form.Item name="ruleId" style={{ minWidth: 200 }}>
        <Select
          allowClear
          placeholder={t('sodRules.violations.filters.rule', 'Quy tac SoD')}
          options={rules.map((r) => ({
            value: r.id,
            label: `${r.permissionCodeA} / ${r.permissionCodeB}`,
          }))}
          showSearch
          filterOption={(input, opt) =>
            (opt?.label as string)?.toLowerCase().includes(input.toLowerCase()) ?? false
          }
        />
      </Form.Item>

      <Form.Item name="dateRange">
        <RangePicker
          placeholder={[
            t('sodRules.violations.filters.from', 'Tu ngay'),
            t('sodRules.violations.filters.to', 'Den ngay'),
          ]}
          allowClear
        />
      </Form.Item>

      <Form.Item>
        <Space>
          <Button
            type="primary"
            icon={<SearchOutlined />}
            onClick={handleSearch}
            loading={loading}
          >
            {t('sodRules.violations.filters.search', 'Tim kiem')}
          </Button>
          <Button icon={<ClearOutlined />} onClick={handleClear}>
            {t('sodRules.violations.filters.clear', 'Xoa loc')}
          </Button>
        </Space>
      </Form.Item>
    </Form>
  );
}
