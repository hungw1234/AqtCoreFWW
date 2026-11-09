// Notifications feature types — mirrors backend DTOs

export interface NotificationDto {
  id: string;
  subject: string;
  body: string;
  isRead: boolean;
  createdAt: string;
  deepLink?: string;
  channel?: string;
}

// BE `/notifications/unread-count` returns a raw integer (ApiResponse<int> envelope, unwrapped by apiClient).
export type UnreadCountDto = number;

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// Notification template for admin management (Liquid templates)
export interface NotificationTemplateDto {
  id: string;
  name: string;
  channel: 'Email' | 'SMS' | 'InApp' | 'Push';
  subject?: string;
  body: string;
  isActive: boolean;
  createdAt: string;
}
