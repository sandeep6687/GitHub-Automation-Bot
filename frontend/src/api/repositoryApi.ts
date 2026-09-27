import { apiFetch } from './client';
import type { ConnectedRepository, AvailableRepository } from '../types/repository';

export const repositoryApi = {
  getAvailableRepositories: (): Promise<AvailableRepository[]> => {
    return apiFetch<AvailableRepository[]>('/api/repositories/available');
  },

  getConnectedRepositories: (): Promise<ConnectedRepository[]> => {
    return apiFetch<ConnectedRepository[]>('/api/repositories');
  },

  connectRepository: (githubRepositoryId: number): Promise<ConnectedRepository> => {
    return apiFetch<ConnectedRepository>('/api/repositories/connect', {
      method: 'POST',
      body: JSON.stringify({ githubRepositoryId }),
    });
  },

  disconnectRepository: (id: string): Promise<void> => {
    return apiFetch<void>(`/api/repositories/${id}`, {
      method: 'DELETE',
    });
  },

  syncGitHubApp: (id: string): Promise<{ appInstalled: boolean; installationId: number | null }> => {
    return apiFetch<{ appInstalled: boolean; installationId: number | null }>(`/api/repositories/${id}/sync-github-app`, {
      method: 'POST',
    });
  },
};
