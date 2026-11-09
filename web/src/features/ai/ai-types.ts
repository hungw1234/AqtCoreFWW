// AI feature types — chat messages and query catalog

// Matches backend ChatRole enum (PascalCase via JsonStringEnumConverter)
export type MessageRole = 'User' | 'Assistant' | 'System';

export interface ChatMessage {
  id: string;
  role: MessageRole;
  content: string;
  timestamp: string;
  queryResults?: QueryResult[];
}

export interface QueryResult {
  columns: string[];
  rows: Record<string, unknown>[];
}

export interface QueryCatalogEntryDto {
  id: string;
  name: string;
  description?: string;
  naturalLanguageQuery: string;
  category?: string;
  createdAt: string;
}

export interface ExecuteQueryResponse {
  sql?: string;
  columns: string[];
  rows: Record<string, unknown>[];
  rowCount: number;
  executionMs?: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
