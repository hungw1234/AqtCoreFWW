// use-chat-signalr — connects to /hubs/chat via SignalR and surfaces real-time messages

import { useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { chatQueryKeys, type ChatMessageDto } from './chat-api';
import { userManager } from '@/features/auth/auth-provider';
import { cleanupAndRedirect } from '@/features/auth/session-error-handler';

// HubConnection singleton per conversation to avoid duplicate connections.
// ref-counts track how many consumers are active for each conversationId.
// When the last consumer unmounts, the connection is stopped and removed.
const connections: Map<string, signalR.HubConnection> = new Map();
const refCounts: Map<string, number> = new Map();

interface UseChatSignalROptions {
  conversationId: string;
  /** Called when a new message arrives from SignalR */
  onMessage: (msg: ChatMessageDto) => void;
}

// useChatSignalR — joins conversation group, invalidates query cache on new messages
export function useChatSignalR({ conversationId, onMessage }: UseChatSignalROptions) {
  const qc = useQueryClient();
  const onMessageRef = useRef(onMessage);
  onMessageRef.current = onMessage;

  useEffect(() => {
    if (!conversationId) return;

    // Reuse existing connection for same conversation
    let conn = connections.get(conversationId);
    if (!conn) {
      conn = new signalR.HubConnectionBuilder()
        // Relative URL → routed via the /hubs proxy (ws:true) same-origin, so no CORS
        // preflight. An absolute VITE_API_BASE_URL made negotiate cross-origin and the
        // browser preflight failed on the x-requested-with header. Mirrors notification-signalr.ts.
        .withUrl('/hubs/chat', {
          // Mid-session expiry: silent renew → on failure drop user so
          // AuthProvider.onUserUnloaded → RouteGuard.signinRedirect.
          accessTokenFactory: async () => {
            let user = await userManager.getUser().catch(() => null);
            if (user?.expired) {
              try {
                user = await userManager.signinSilent();
              } catch {
                await cleanupAndRedirect();
                return '';
              }
            }
            return user?.access_token ?? '';
          },
        })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();
      connections.set(conversationId, conn);
    }

    // Increment ref count — tracks how many component instances use this connection
    refCounts.set(conversationId, (refCounts.get(conversationId) ?? 0) + 1);

    // Register message handler
    const handleMessage = (msg: ChatMessageDto) => {
      if (msg.conversationId !== conversationId) return;
      onMessageRef.current(msg);
      // Invalidate to sync any missed messages
      qc.invalidateQueries({ queryKey: chatQueryKeys.messages(conversationId, {}) });
      qc.invalidateQueries({ queryKey: chatQueryKeys.conversations });
    };

    conn.on('ReceiveMessage', handleMessage);

    // Real-time "seen by": another participant read a message → refresh that message's receipts.
    const handleReadReceipt = (messageId: string) => {
      qc.invalidateQueries({ queryKey: chatQueryKeys.readReceipts(messageId) });
    };
    conn.on('ReceiveReadReceipt', handleReadReceipt);

    // Start if not already connected
    if (conn.state === signalR.HubConnectionState.Disconnected) {
      conn
        .start()
        .then(() => conn!.invoke('JoinConversation', conversationId).catch(() => {}))
        .catch(() => {});
    }

    return () => {
      conn!.off('ReceiveMessage', handleMessage);
      conn!.off('ReceiveReadReceipt', handleReadReceipt);
      // Decrement ref count; stop connection only when the last consumer leaves.
      const count = (refCounts.get(conversationId) ?? 1) - 1;
      refCounts.set(conversationId, count);
      if (count <= 0) {
        // Defer teardown by a microtask. React StrictMode (dev) and rapid conversation
        // switching unmount→remount synchronously, re-incrementing the ref count on the
        // next mount. Stopping immediately would abort an in-flight negotiate and surface
        // "The connection was stopped during negotiation". Only stop if still idle next tick.
        queueMicrotask(() => {
          if ((refCounts.get(conversationId) ?? 0) <= 0) {
            conn!.stop().catch(() => {});
            connections.delete(conversationId);
            refCounts.delete(conversationId);
          }
        });
      }
    };
  }, [conversationId, qc]);
}
