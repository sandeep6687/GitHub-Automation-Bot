import { apiFetch } from './client';
import type { ActivityResponse } from '../types/activity';

export interface ActivityFilterParams {
  limit?: number;
  status?: string;
  eventType?: string;
}

export const activityApi = {
  getActivity: (repositoryId: string, params: ActivityFilterParams = {}): Promise<ActivityResponse> => {
    const query = new URLSearchParams();
    if (params.limit) query.set('limit', params.limit.toString());
    if (params.status) query.set('status', params.status);
    if (params.eventType) query.set('eventType', params.eventType);

    const qs = query.toString();
    const endpoint = `/api/repositories/${repositoryId}/activity${qs ? `?${qs}` : ''}`;
    return apiFetch<ActivityResponse>(endpoint);
  },
};
