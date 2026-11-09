// ProvinceDto — top-level administrative division
export interface ProvinceDto {
  id: string;
  code: string;
  name: string;
  districtCount?: number;
}

// DistrictDto — second-level division, belongs to a province
export interface DistrictDto {
  id: string;
  code: string;
  name: string;
  provinceId: string;
  wardCount?: number;
}

// WardDto — third-level division, belongs to a district
export interface WardDto {
  id: string;
  code: string;
  name: string;
  districtId: string;
}
