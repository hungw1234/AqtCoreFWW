// Chat API hooks — wraps /api/v1/chat/* endpoints (P2 collaboration module)

import { useQuery, useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/core/api';
import type { PaginationParams } from '@/shared/types/api';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ConversationDto {
  id: string;
  type: string;
  name?: string;
  createdBy: string;
  lastMessageAt?: string;
  isArchived: boolean;
  memberCount: number;
  lastMessagePreview?: string;
  lastMessageSenderName?: string;
  unreadCount: number;
  participantNames?: string[];
}

export interface ChatMessageDto {
  id: string;
  conversationId: string;
  senderId: string;
  senderName?: string;
  content: string;
  createdAt: string;
}

/** A single "seen by" read-receipt — who read a message and when. */
export interface ReadReceiptDto {
  userId: string;
  fullName?: string;
  readAt: string;
}

/**
 * Display names of everyone who read a message, excluding the current user (you don't show "seen by
 * yourself"). Falls back to a short id slice when a reader's name is unavailable. Pure — unit-tested.
 */
export function readerNames(
  receipts: ReadReceiptDto[] | undefined,
  excludeUserId: string,
): string[] {
  return (receipts ?? [])
    .filter((r) => r.userId !== excludeUserId)
    .map((r) => r.fullName?.trim() || r.userId.slice(0, 8));
}

export interface CreateConversationRequest {
  type: 'Direct' | 'Group';
  name?: string;
  memberUserIds: string[];
}

export interface SendMessageRequest {
  content: string;
  contentType?: 'Text' | 'Html' | 'Markdown';
  replyToId?: string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const chatQueryKeys = {
  all: ['chat'] as const,
  conversations: ['chat', 'conversations'] as const,
  messages: (id: string, params: PaginationParams) => ['chat', 'messages', id, params] as const,
  readReceipts: (messageId: string) => ['chat', 'read-receipts', messageId] as const,
};

// ─── Queries ──────────────────────────────────────────────────────────────────

const CONVERSATIONS_PAGE_SIZE = 20;

/** GET /api/v1/conversations — infinite "load more" pagination */
export function useConversations() {
  return useInfiniteQuery({
    queryKey: chatQueryKeys.conversations,
    queryFn: ({ pageParam }) =>
      apiClient
        .get<ConversationDto[]>('/conversations', { params: { page: pageParam, pageSize: CONVERSATIONS_PAGE_SIZE } })
        .then((r) => r.data),
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.length === CONVERSATIONS_PAGE_SIZE ? allPages.length + 1 : undefined,
  });
}

/** GET /api/v1/conversations/{id}/messages — BE returns flat array, not PaginatedResult */
export function useChatMessages(conversationId: string, params: PaginationParams = {}) {
  return useQuery({
    queryKey: chatQueryKeys.messages(conversationId, params),
    queryFn: () =>
      apiClient
        .get<ChatMessageDto[]>(`/conversations/${conversationId}/messages`, { params })
        .then((r) => r.data),
    enabled: Boolean(conversationId),
  });
}

/**
 * GET /api/v1/messages/{id}/read-receipts — who has seen a message.
 * Queried only for the newest message (the FE marks just the last message as read), so this is one
 * request, not one per message. Disabled until a messageId is available.
 */
export function useMessageReadReceipts(messageId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: chatQueryKeys.readReceipts(messageId ?? ''),
    queryFn: () =>
      apiClient.get<ReadReceiptDto[]>(`/messages/${messageId}/read-receipts`).then((r) => r.data),
    enabled: Boolean(messageId) && enabled,
    staleTime: 30_000,
  });
}

// ─── Mutations ────────────────────────────────────────────────────────────────

/** POST /api/v1/conversations */
export function useCreateConversation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateConversationRequest) =>
      apiClient.post<ConversationDto>('/conversations', body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: chatQueryKeys.conversations }),
  });
}

/** POST /api/v1/messages/{id}/read — marks message as read, updates unread count */
export function useMarkAsRead() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ messageId, conversationId }: { messageId: string; conversationId: string }) =>
      apiClient.post(`/messages/${messageId}/read`, { conversationId }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: chatQueryKeys.conversations });
      // Refresh "seen by" for the message just marked (our own receipt now exists).
      qc.invalidateQueries({ queryKey: chatQueryKeys.readReceipts(variables.messageId) });
    },
  });
}

/** POST /api/v1/messages — BE route is /messages, not /conversations/{id}/messages */
export function useSendMessage(conversationId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: SendMessageRequest) =>
      apiClient
        .post<ChatMessageDto>('/messages', {
          conversationId,
          content: body.content,
          contentType: body.contentType ?? 'Text',
          replyToId: body.replyToId ?? null,
        })
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: chatQueryKeys.messages(conversationId, {}) });
      qc.invalidateQueries({ queryKey: chatQueryKeys.conversations });
    },
  });
}
