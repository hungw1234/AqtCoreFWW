// Files feature types — mirrors backend DTOs

export type VirusScanStatus = 'Pending' | 'Clean' | 'Infected' | 'Failed';

// Field names are camelCase — the list endpoint serializes through System.Text.Json (MVC default
// camelCase policy), identical to every other API DTO. The handler's anonymous-object projection has
// PascalCase property names server-side, but STJ lowercases them on the wire. (VirusScanStatus VALUES
// below stay PascalCase — those are data emitted by the SQL CASE, not JSON keys.)
export interface FileRecordDto {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedBy: string;
  uploadedByName?: string | null;
  uploadedAt: string;
  virusScanStatus: VirusScanStatus;
  isDigitallySigned?: boolean;
}

// Async upload response — POST /files returns 202 (two-phase: quarantine → ClamAV scan).
// Full metadata (OriginalFileName, ContentType, scan status) is fetched later via GET /files/{id}/metadata.
export interface FileUploadResultDto {
  fileId: string;
  statusUrl: string;
  message: string;
}

// File availability lifecycle (BE FileStatus enum, serialized as string). Quarantined = scan pending
// (not downloadable); Available = scan passed; Rejected = virus found (deleted, metadata retained).
export type FileStatus = 'Quarantined' | 'Available' | 'Rejected';

// GET /files/{id}/metadata — record serialized camelCase. Used to poll scan status after upload.
export interface FileMetadataDto {
  id: string;
  originalFileName: string;
  contentType: string;
  sizeBytes: number;
  status: FileStatus;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Human-readable file size (e.g. "1.2 MB") */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
