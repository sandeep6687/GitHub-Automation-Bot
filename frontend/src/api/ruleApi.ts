import { apiFetch } from './client';
import type { Rule, CreateRuleDto, UpdateRuleDto } from '../types/rule';

export const ruleApi = {
  getRules: (repositoryId: string): Promise<Rule[]> => {
    return apiFetch<Rule[]>(`/api/repositories/${repositoryId}/rules`);
  },

  getRule: (repositoryId: string, ruleId: string): Promise<Rule> => {
    return apiFetch<Rule>(`/api/repositories/${repositoryId}/rules/${ruleId}`);
  },

  createRule: (repositoryId: string, data: CreateRuleDto): Promise<Rule> => {
    return apiFetch<Rule>(`/api/repositories/${repositoryId}/rules`, {
      method: 'POST',
      body: JSON.stringify(data),
    });
  },

  updateRule: (repositoryId: string, ruleId: string, data: UpdateRuleDto): Promise<Rule> => {
    return apiFetch<Rule>(`/api/repositories/${repositoryId}/rules/${ruleId}`, {
      method: 'PUT',
      body: JSON.stringify(data),
    });
  },

  toggleRule: (repositoryId: string, ruleId: string, enabled: boolean): Promise<Rule> => {
    return apiFetch<Rule>(`/api/repositories/${repositoryId}/rules/${ruleId}/enabled`, {
      method: 'PATCH',
      body: JSON.stringify({ enabled }),
    });
  },

  deleteRule: (repositoryId: string, ruleId: string): Promise<void> => {
    return apiFetch<void>(`/api/repositories/${repositoryId}/rules/${ruleId}`, {
      method: 'DELETE',
    });
  },
};
