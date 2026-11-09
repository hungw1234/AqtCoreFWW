// Notification template DTO — mirrors NotificationTemplateDto on the BE
export interface NotificationTemplateDto {
  id: string;
  tenantId: string;
  templateKey: string;
  channel: string;
  subjectTemplate: string;
  bodyTemplate: string;
  isDefault: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateNotificationTemplatePayload {
  templateKey: string;
  subjectTemplate: string;
  bodyTemplate: string;
  channel: string;
}

export interface UpdateNotificationTemplatePayload {
  subjectTemplate: string;
  bodyTemplate: string;
}
