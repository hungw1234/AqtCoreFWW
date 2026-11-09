// Data classification types — mirrors AqtCoreFW.SharedKernel.Compliance.DataClassificationLevel
// and AqtCoreFW.Audit.Application.DTOs.*
// Enum values: Public=0, Internal=1, Confidential=2, Restricted=3

export const DataClassificationLevel = {
  Public: 0,
  Internal: 1,
  Confidential: 2,
  Restricted: 3,
} as const;
export type DataClassificationLevel = (typeof DataClassificationLevel)[keyof typeof DataClassificationLevel];

// ClassifiedFieldDto — single property found by scanner scan
export interface ClassifiedField {
  assemblyName: string;
  typeFullName: string;
  propertyName: string;
  propertyType: string;
  level: DataClassificationLevel;
  isEncryptedAtRest: boolean;
}

// ClassificationInventoryDto — full inventory report returned by GET /classifications
export interface ClassificationInventory {
  scannedAt: string; // ISO 8601
  totalCount: number;
  restrictedCount: number;
  confidentialCount: number;
  entries: ClassifiedField[];
}

// ClassificationRuleDto — handling rule for one classification level
export interface ClassificationRule {
  level: DataClassificationLevel;
  mustEncryptAtRest: boolean;
  allowExport: boolean;
  maxRetentionDays: number | null;
  updatedBy: string;
  updatedAt: string; // ISO 8601
  rowVersion: string; // base64 — sent back as-is in UpdateClassificationRuleRequest.RowVersion
}

// UpdateClassificationRuleRequest — PUT /classification-rules/{level} body
// BE record: bool MustEncryptAtRest, bool AllowExport, int? MaxRetentionDays, string RowVersion
export interface UpdateClassificationRuleRequest {
  mustEncryptAtRest: boolean;
  allowExport: boolean;
  maxRetentionDays: number | null;
  rowVersion: string; // base64
}
