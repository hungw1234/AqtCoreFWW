import type { NotificationInstance } from 'antd/es/notification/interface';
import type { MessageInstance } from 'antd/es/message/interface';

// Bridge: allows non-React code (query-config) AND mass consumers to use App-scoped
// notification/message. Antd v5 deprecates the static `message.*` API because it cannot
// consume theme/locale context — calls must go through App.useApp() or a bridged hook-
// captured instance. Setting the bridge from a React-tree component preserves all
// context (theme, locale, dark mode, motion) for every consumer.
let notificationInstance: NotificationInstance | null = null;
let messageInstance: MessageInstance | null = null;

export function setNotificationInstance(n: NotificationInstance) {
  notificationInstance = n;
}

export function getNotificationInstance(): NotificationInstance | null {
  return notificationInstance;
}

export function setMessageInstance(m: MessageInstance) {
  messageInstance = m;
}

export function getMessageInstance(): MessageInstance | null {
  return messageInstance;
}

// Ergonomic facade — mirrors the static `message.*` surface so call sites stay identical
// after migration. Each method no-ops with a console.warn when the bridge is not yet
// populated (e.g. error fired during module init before InnerProviders mounts), which
// is strictly better than the silent failure of a null deref.
export const appMessage = {
  success: (content: Parameters<MessageInstance['success']>[0], duration?: number) => {
    const m = messageInstance;
    if (!m) { console.warn('[appMessage] not yet initialized:', content); return; }
    return m.success(content, duration);
  },
  error: (content: Parameters<MessageInstance['error']>[0], duration?: number) => {
    const m = messageInstance;
    if (!m) { console.warn('[appMessage] not yet initialized:', content); return; }
    return m.error(content, duration);
  },
  warning: (content: Parameters<MessageInstance['warning']>[0], duration?: number) => {
    const m = messageInstance;
    if (!m) { console.warn('[appMessage] not yet initialized:', content); return; }
    return m.warning(content, duration);
  },
  info: (content: Parameters<MessageInstance['info']>[0], duration?: number) => {
    const m = messageInstance;
    if (!m) { console.warn('[appMessage] not yet initialized:', content); return; }
    return m.info(content, duration);
  },
  loading: (content: Parameters<MessageInstance['loading']>[0], duration?: number) => {
    const m = messageInstance;
    if (!m) { console.warn('[appMessage] not yet initialized:', content); return; }
    return m.loading(content, duration);
  },
  open: (config: Parameters<MessageInstance['open']>[0]) => {
    const m = messageInstance;
    if (!m) { console.warn('[appMessage] not yet initialized'); return; }
    return m.open(config);
  },
  destroy: (key?: Parameters<MessageInstance['destroy']>[0]) => {
    messageInstance?.destroy(key);
  },
};
