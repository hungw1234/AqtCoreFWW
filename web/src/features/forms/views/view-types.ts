// view-types.ts — DTO interfaces for the Views module (mirrors BE ViewDefinition entity)

export type ViewType = 'List' | 'Grid' | 'Kanban' | 'Calendar';
export type ColumnFormatter = 'Text' | 'Date' | 'Currency' | 'Badge' | 'Link';
export type FilterOperator = 'Equals' | 'Contains' | 'GreaterThan' | 'LessThan' | 'Between' | 'In';

export interface ViewColumnDto {
  fieldName: string;
  label?: string;
  displayOrder: number;
  sortable: boolean;
  filterable: boolean;
  width?: number;
  formatter?: ColumnFormatter;
}

export interface ViewFilterDto {
  fieldName: string;
  operator: FilterOperator;
  defaultValue?: string;
  isRequired: boolean;
}

export interface ViewDefinitionDto {
  id: string;
  name: string;
  entityType: string;
  type: ViewType;
  defaultSortField?: string;
  defaultSortDir?: string;
  pageSize?: number;
  isDefault?: boolean;
  columns: ViewColumnDto[];
  filters: ViewFilterDto[];
  createdAt?: string;
}

export interface CreateViewPayload {
  name: string;
  entityType: string;
  type: ViewType;
  defaultSortField?: string;
  defaultSortDir?: string;
  pageSize?: number;
  isDefault?: boolean;
  columns?: Array<{
    fieldName: string;
    label?: string;
    displayOrder: number;
    sortable: boolean;
    filterable: boolean;
    width?: number;
    formatter?: string;
  }>;
  filters?: Array<{
    fieldName: string;
    operator: string;
    defaultValue?: string;
    isRequired: boolean;
  }>;
}

export interface UpdateViewPayload {
  name?: string;
  defaultSortField?: string;
  defaultSortDir?: string;
  pageSize?: number;
  isDefault?: boolean;
  columns?: Array<{
    fieldName: string;
    label?: string;
    displayOrder: number;
    sortable: boolean;
    filterable: boolean;
    width?: number;
    formatter?: string;
  }>;
}
