import { useEffect } from 'react';
import { Badge, Popover, List, Typography, Button, Tag, App as AntApp } from 'antd';
import { BellOutlined, CheckOutlined } from '@ant-design/icons';
import { useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { useUnreadCount, useNotifications, useMarkAsRead, useMarkAllAsRead } from './notification-api';
import { startSignalR, onNotification } from './notification-signalr';
import { useQueryClient } from '@tanstack/react-query';
import type { NotificationDto } from './notification-types';

const { Text } = Typography;

// NotificationBell — topbar bell icon with unread badge and recent notifications popover
export function NotificationBell() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { notification } = AntApp.useApp();
  const { data: unreadData } = useUnreadCount();
  const { data: recentData } = useNotifications(1, 10);
  const { mutate: markRead } = useMarkAsRead();
  const { mutate: markAll, isPending: isMarkingAll } = useMarkAllAsRead();

  const unreadCount = unreadData ?? 0;
  // BE orders: unread first, then newest first (ORDER BY IsRead ASC, CreatedAt DESC).
  const notifications = recentData?.items ?? [];

  // Start SignalR connection and refresh counts on new notification
  useEffect(() => {
    startSignalR();
    const unsub = onNotification((n) => {
      notification.info({ message: n.title, description: n.body, placement: 'topRight', duration: 5 });
      queryClient.invalidateQueries({ queryKey: ['notifications'] });
    });
    return unsub;
  }, [queryClient]);

  function handleClick(n: NotificationDto) {
    if (!n.isRead) markRead(n.id);
    // L-01: Only navigate to relative paths — prevent open redirect via server-provided deepLink
    if (n.deepLink?.startsWith('/')) navigate({ to: n.deepLink });
  }

  // Popover body: header (title) + scrollable list (capped ~50vh) + footer (view all).
  // Scroll container is the List wrapper — footer stays visible while long lists scroll.
  const popoverContent = (
    <div style={{ width: 340, display: 'flex', flexDirection: 'column' }}>
      <div style={{ maxHeight: '40vh', overflowY: 'auto', marginRight: -4, paddingRight: 4 }}>
        <List
          size="small"
          dataSource={notifications}
          locale={{ emptyText: t('page.notifications.empty') }}
          renderItem={(n) => (
            <List.Item
              role="button"
              tabIndex={0}
              style={{ cursor: 'pointer', opacity: n.isRead ? 0.6 : 1, padding: '8px 4px' }}
              onClick={() => handleClick(n)}
              onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); handleClick(n); } }}
            >
              <List.Item.Meta
                title={
                  <span style={{ fontSize: 13 }}>
                    {!n.isRead && <Tag color="blue" style={{ marginRight: 6 }}>{t('page.notifications.newTag')}</Tag>}
                    <Text strong={!n.isRead}>{n.subject}</Text>
                  </span>
                }
                description={
                  <span>
                    <Text type="secondary" style={{ fontSize: 12, display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>
                      {n.body}
                    </Text>
                    <Text type="secondary" style={{ fontSize: 11 }}>
                      {dayjs(n.createdAt).format('DD/MM/YYYY HH:mm')}
                    </Text>
                  </span>
                }
              />
            </List.Item>
          )}
        />
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingTop: 8, borderTop: '1px solid #f0f0f0' }}>
        <Button
          type="link"
          size="small"
          icon={<CheckOutlined />}
          loading={isMarkingAll}
          disabled={unreadCount === 0}
          onClick={() => markAll(undefined, {
            onSuccess: () => notification.success({ message: t('page.notifications.markAllReadSuccess', { defaultValue: 'Đã đánh dấu tất cả đã đọc' }), placement: 'topRight', duration: 2 }),
            onError: () => notification.error({ message: t('common.operationFailed', { defaultValue: 'Thao tác thất bại' }), placement: 'topRight' }),
          })}
        >
          {t('page.notifications.markAllRead', { defaultValue: 'Đánh dấu tất cả đã đọc' })}
        </Button>
        <Button type="link" size="small" onClick={() => navigate({ to: '/notifications' })}>
          {t('page.notifications.viewAll')}
        </Button>
      </div>
    </div>
  );

  return (
    <Popover
      content={popoverContent}
      title={t('page.notifications.title')}
      trigger="click"
      placement="bottomRight"
    >
      <Button
        type="text"
        title={t('nav.notifications', { defaultValue: 'Thông báo' })}
        aria-label={t('nav.notifications', { defaultValue: 'Thông báo' })}
        style={{
          color: 'var(--gov-text-secondary)',
          fontSize: 18,
          width: 36,
          height: 36,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Badge count={unreadCount} size="small" offset={[0, 0]}>
          <BellOutlined style={{ fontSize: 18, color: 'var(--gov-text-secondary)' }} />
        </Badge>
      </Button>
    </Popover>
  );
}
