import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginatedResult, PaginationParams } from '@/shared/types/api';

// ─── Types ───────────────────────────────────────────────────────────────────

export type PartnerStatus = 'Active' | 'Suspended' | 'Deactivated';
export type ContractStatus = 'Draft' | 'Active' | 'Expired' | 'Terminated';
export type MessageDirection = 'Inbound' | 'Outbound';
export type MessageLogStatus = 'Sent' | 'Delivered' | 'Failed' | 'Acknowledged';

export interface PartnerDto {
  id: string;
  tenantId: string;
  name: string;
  code: string;
  contactEmail?: string;
  contactPhone?: string;
  endpoint?: string;
  authScheme?: string;
  status: PartnerStatus;
  createdAt: string;
  updatedAt?: string;
}

export interface ContractDto {
  id: string;
  tenantId: string;
  partnerId: string;
  title: string;
  description?: string;
  effectiveDate: string;
  expiryDate?: string;
  status: ContractStatus;
  dataScopeJson?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface MessageLogDto {
  id: string;
  tenantId: string;
  partnerId: string;
  contractId?: string;
  direction: MessageDirection;
  messageType: string;
  payload?: string;
  status: MessageLogStatus;
  correlationId?: string;
  sentAt: string;
  acknowledgedAt?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface CreatePartnerDto {
  name: string;
  code: string;
  contactEmail?: string;
  contactPhone?: string;
  endpoint?: string;
  authScheme?: string;
}

export type UpdatePartnerDto = CreatePartnerDto;

// Request body for POST /integration/contracts.
// Field name mirrors BE CreateContractBody (T2 PartnerPublicId Guid AK).
export interface CreateContractDto {
  partnerPublicId: string;
  title: string;
  description?: string;
  effectiveDate: string;
  expiryDate?: string;
  dataScopeJson?: string;
}

// Request body for POST /integration/message-logs.
// Field names mirror BE CreateMessageLogBody (T2 PartnerPublicId/ContractPublicId Guid AKs).
export interface CreateMessageLogDto {
  partnerPublicId: string;
  contractPublicId?: string;
  direction: MessageDirection;
  messageType: string;
  payload?: string;
  correlationId?: string;
}

// ─── Query keys ──────────────────────────────────────────────────────────────

export const integrationKeys = {
  partners: ['integration', 'partners'] as const,
  partnerList: (p: PaginationParams) => ['integration', 'partners', 'list', p] as const,
  partnerDetail: (id: string) => ['integration', 'partners', 'detail', id] as const,
  contracts: ['integration', 'contracts'] as const,
  contractList: (p: PaginationParams & { partnerPublicId?: string }) =>
    ['integration', 'contracts', 'list', p] as const,
  contractDetail: (id: string) => ['integration', 'contracts', 'detail', id] as const,
  messageLogs: ['integration', 'messageLogs'] as const,
  messageLogList: (
    p: PaginationParams & { partnerPublicId?: string; contractPublicId?: string },
  ) => ['integration', 'messageLogs', 'list', p] as const,
  messageLogDetail: (id: string) => ['integration', 'messageLogs', 'detail', id] as const,
};

// ─── Partner hooks ────────────────────────────────────────────────────────────

export function usePartners(params: PaginationParams = {}) {
  return useQuery({
    queryKey: integrationKeys.partnerList(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<PartnerDto>>('/integration/partners', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

export function usePartner(id: string) {
  return useQuery({
    queryKey: integrationKeys.partnerDetail(id),
    queryFn: () =>
      apiClient.get<PartnerDto>(`/integration/partners/${id}`).then((r) => r.data),
    enabled: Boolean(id),
  });
}

export function useCreatePartner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreatePartnerDto) =>
      apiClient.post<PartnerDto>('/integration/partners', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.partners }),
  });
}

export function useUpdatePartner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: UpdatePartnerDto & { id: string }) =>
      apiClient.put<PartnerDto>(`/integration/partners/${id}`, body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.partners }),
  });
}

export function useDeletePartner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete(`/integration/partners/${id}`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.partners }),
  });
}

export function useSuspendPartner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/integration/partners/${id}/suspend`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.partners }),
  });
}

export function useActivatePartner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/integration/partners/${id}/activate`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.partners }),
  });
}

// ─── Contract hooks ───────────────────────────────────────────────────────────

export function useContracts(params: PaginationParams & { partnerPublicId?: string } = {}) {
  return useQuery({
    queryKey: integrationKeys.contractList(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<ContractDto>>('/integration/contracts', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

export function useContract(id: string) {
  return useQuery({
    queryKey: integrationKeys.contractDetail(id),
    queryFn: () =>
      apiClient.get<ContractDto>(`/integration/contracts/${id}`).then((r) => r.data),
    enabled: Boolean(id),
  });
}

export function useCreateContract() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateContractDto) =>
      apiClient.post<ContractDto>('/integration/contracts', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.contracts }),
  });
}

export function useActivateContract() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/integration/contracts/${id}/activate`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.contracts }),
  });
}

export function useTerminateContract() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiClient.post(`/integration/contracts/${id}/terminate`).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.contracts }),
  });
}

// ─── MessageLog hooks ─────────────────────────────────────────────────────────

export function useMessageLogs(
  params: PaginationParams & { partnerPublicId?: string; contractPublicId?: string } = {},
) {
  return useQuery({
    queryKey: integrationKeys.messageLogList(params),
    queryFn: () =>
      apiClient
        .get<PaginatedResult<MessageLogDto>>('/integration/message-logs', { params })
        .then((r) => r.data),
    placeholderData: (prev) => prev,
  });
}

export function useMessageLog(id: string) {
  return useQuery({
    queryKey: integrationKeys.messageLogDetail(id),
    queryFn: () =>
      apiClient.get<MessageLogDto>(`/integration/message-logs/${id}`).then((r) => r.data),
    enabled: Boolean(id),
  });
}

export function useLogMessage() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateMessageLogDto) =>
      apiClient.post<MessageLogDto>('/integration/message-logs', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: integrationKeys.messageLogs }),
  });
}
