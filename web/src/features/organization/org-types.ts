// OrgUnitDto — matches backend OrgUnit entity
export interface OrgUnitDto {
  id: string;
  name: string;
  code: string;
  parentId?: string;
  level: number;
  childCount: number;
  staffCount?: number;
  description?: string;
}

// CreateOrgUnitRequest — POST /api/v1/org-units
export interface CreateOrgUnitRequest {
  name: string;
  code: string;
  parentId?: string;
  description?: string;
}

// UpdateOrgUnitRequest — PUT /api/v1/org-units/{id}
export interface UpdateOrgUnitRequest {
  name?: string;
  code?: string;
  parentId?: string;
  description?: string;
}

// StaffAssignmentDto — matches BE UserOrgUnitAssignmentDto
export interface StaffAssignmentDto {
  id: string;
  userId: string;
  fullName?: string;
  email?: string;
  roleInOrg: string;
  validFrom: string;
  validTo?: string;
  isActive: boolean;
}

// Ant Design Tree node shape built from OrgUnitDto
export interface OrgTreeNode {
  key: string;
  title: string;
  children?: OrgTreeNode[];
  isLeaf: boolean;
  data: OrgUnitDto;
}
