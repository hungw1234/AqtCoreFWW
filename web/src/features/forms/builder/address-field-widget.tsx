// address-field-widget.tsx — cascading province → ward selects from the AdministrativeUnit API.
// The address model is 2-level (Tỉnh → Phường); the district level was removed with the hệ-34
// administrative reform. Both levels read the AdministrativeUnit tree via /masterdata/admin-units
// (level 1 = provinces, level 2 = wards scoped by parentCode).
import { useCallback } from 'react';
import { Select, Space } from 'antd';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/core/api';
import { foldDiacritics } from '@/shared/utils';

interface AddressValue {
  province?: string;
  ward?: string;
}

interface AddressFieldWidgetProps {
  value?: AddressValue;
  onChange?: (value: AddressValue) => void;
  disabled?: boolean;
}

interface MasterDataItem {
  code: string;
  nameVi: string;
}

function useProvinces() {
  return useQuery({
    queryKey: ['masterdata', 'admin-units', 1],
    queryFn: () =>
      apiClient
        .get<MasterDataItem[]>('/masterdata/admin-units', { params: { level: 1 } })
        .then((r) => r.data),
    staleTime: 30 * 60 * 1000,
  });
}

function useWards(provinceCode?: string) {
  return useQuery({
    queryKey: ['masterdata', 'admin-units', 2, provinceCode],
    queryFn: () =>
      apiClient
        .get<MasterDataItem[]>('/masterdata/admin-units', {
          params: { level: 2, parentCode: provinceCode },
        })
        .then((r) => r.data),
    enabled: Boolean(provinceCode),
    staleTime: 30 * 60 * 1000,
  });
}

// Diacritic-insensitive so "nghe" matches "Nghệ An" across all 34 provinces /
// up to ~166 wards (raw .includes was unusable at hệ-34 scale).
const filterOption = (input: string, option?: { label: string }) =>
  foldDiacritics(option?.label).includes(foldDiacritics(input));

export function AddressFieldWidget({ value, onChange, disabled }: AddressFieldWidgetProps) {
  const { t } = useTranslation();
  const { data: provinces, isLoading: pLoading, isError: pError } = useProvinces();
  const { data: wards, isLoading: wLoading, isError: wError } = useWards(value?.province);

  const handleProvince = useCallback(
    (code: string) => onChange?.({ province: code, ward: undefined }),
    [onChange]
  );

  const handleWard = useCallback(
    (code: string) => onChange?.({ province: value?.province, ward: code }),
    [onChange, value?.province]
  );

  return (
    <Space.Compact style={{ width: '100%' }}>
      <Select
        style={{ width: '50%' }}
        placeholder={pError ? t('forms.address.loadError') : t('forms.address.province')}
        value={value?.province}
        onChange={handleProvince}
        loading={pLoading}
        disabled={disabled}
        status={pError ? 'error' : undefined}
        showSearch
        filterOption={filterOption}
        options={provinces?.map((p) => ({ value: p.code, label: p.nameVi }))}
        allowClear
      />
      <Select
        style={{ width: '50%' }}
        placeholder={wError ? t('forms.address.loadError') : t('forms.address.ward')}
        value={value?.ward}
        onChange={handleWard}
        loading={wLoading}
        disabled={disabled || !value?.province}
        status={wError ? 'error' : undefined}
        showSearch
        filterOption={filterOption}
        options={wards?.map((w) => ({ value: w.code, label: w.nameVi }))}
        allowClear
      />
    </Space.Compact>
  );
}
