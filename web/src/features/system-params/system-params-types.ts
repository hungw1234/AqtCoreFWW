// SystemParamDto — matches backend SystemParam entity
export interface SystemParamDto {
  key: string;
  value: string;
  description?: string;
  category?: string;
  isEncrypted: boolean;
  updatedAt?: string;
  updatedBy?: string;
}

// UpdateSystemParamRequest — PUT /api/v1/system-params/{key}
export interface UpdateSystemParamRequest {
  value: string;
  description?: string;
}

// FeatureFlagDto — matches backend FeatureFlag entity
export interface FeatureFlagDto {
  id: string;
  name: string;
  key: string;
  description?: string;
  isEnabled: boolean;
  rolloutPercentage: number;   // 0–100
  updatedAt?: string;
}

// UpdateFeatureFlagRequest — PUT /api/v1/feature-flags/{id}
export interface UpdateFeatureFlagRequest {
  isEnabled: boolean;
  rolloutPercentage?: number;
}

// AnnouncementDto — matches backend Announcement entity
export type AnnouncementStatus = 'Draft' | 'Active' | 'Scheduled' | 'Expired';

export interface AnnouncementDto {
  id: string;
  title: string;
  content: string;
  status: AnnouncementStatus;
  startDate?: string;
  endDate?: string;
  createdAt: string;
  updatedAt?: string;
}

// CreateAnnouncementRequest — POST /api/v1/announcements
export interface CreateAnnouncementRequest {
  title: string;
  content: string;
  startDate?: string;
  endDate?: string;
}
