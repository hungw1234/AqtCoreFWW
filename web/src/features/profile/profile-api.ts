import { useMutation } from '@tanstack/react-query';
import { apiClient } from '@/core/api';

/** POST /api/v1/account/change-password — self-service password change */
export function useChangePassword() {
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) =>
      apiClient.post('/account/change-password', body).then((r) => r.data),
  });
}
