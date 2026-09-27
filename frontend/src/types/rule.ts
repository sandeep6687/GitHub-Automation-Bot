export interface RuleCondition {
  id?: string;
  conditionType: 'TitleContains' | 'AuthorEquals' | 'LabelContains';
  field?: string;
  value: string;
  caseSensitive?: boolean;
}

export interface RuleAction {
  id?: string;
  actionType: 'AddLabel' | 'AddComment' | 'SlackNotify' | 'AiTriage';
  executionOrder: number;
  configuration: Record<string, any>;
}

export interface Rule {
  id: string;
  repositoryId: string;
  name: string;
  eventType: string;
  enabled: boolean;
  priority: number;
  conditions: RuleCondition[];
  actions: RuleAction[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateRuleDto {
  name: string;
  eventType: string;
  priority: number;
  conditions: RuleCondition[];
  actions: RuleAction[];
}

export interface UpdateRuleDto {
  name: string;
  eventType: string;
  priority: number;
  isEnabled: boolean;
  conditions: RuleCondition[];
  actions: RuleAction[];
}
