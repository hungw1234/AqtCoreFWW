// Report feature types — mirrors backend DTOs

export interface ReportDefinitionDto {
  id: string;
  name: string;
  nameVi: string;
  description: string;
  parametersSchema: string; // JSON string describing dynamic params
  outputFormat: OutputFormat;
  isActive: boolean;
  tenantId?: string;
  createdAt: string;
}

export type OutputFormat = 'Excel' | 'Pdf';

export type ExecutionStatus = 'Queued' | 'Running' | 'Done' | 'Failed';

export interface ReportExecutionDto {
  id: string;
  reportDefinitionId: string;
  parameters: string;
  requestedBy: string;
  requestedAt: string;
  status: ExecutionStatus;
  startedAt?: string;
  completedAt?: string;
  hasResult: boolean;
  errorMessage?: string;
}

// Request body for creating a report definition
export interface CreateReportDefinitionRequest {
  name: string;
  nameVi: string;
  description: string;
  sqlTemplate: string;
  parametersSchema: string;
  outputFormat: OutputFormat;
}

// Request body for running a report
export interface RunReportRequest {
  reportDefinitionId: string;
  parametersJson: string;
  formatOverride?: OutputFormat;
}
