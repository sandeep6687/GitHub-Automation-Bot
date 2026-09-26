import { apiFetch, getAuthLoginUrl } from './client';
import type { UserProfile } from '../types/auth';

export const authApi = {
  getCurrentUser: (): Promise<UserProfile> => {
    return apiFetch<UserProfile>('/api/auth/me');
  },

  logout: (): Promise<{ message: string }> => {
    return apiFetch<{ message: string }>('/api/auth/logout', {
      method: 'POST',
    });
  },

  getLoginUrl: getAuthLoginUrl,
};
