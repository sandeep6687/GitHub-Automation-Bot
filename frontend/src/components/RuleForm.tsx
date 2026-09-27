import React, { useState } from 'react';
import type { Rule, CreateRuleDto, UpdateRuleDto, RuleCondition, RuleAction } from '../types/rule';

interface RuleFormProps {
  initialRule?: Rule | null;
  onSubmit: (data: CreateRuleDto | UpdateRuleDto) => void;
  onCancel: () => void;
  isProcessing?: boolean;
}

export const RuleForm: React.FC<RuleFormProps> = ({
  initialRule,
  onSubmit,
  onCancel,
  isProcessing = false,
}) => {
  const [name, setName] = useState(initialRule?.name || '');
  const [eventType, setEventType] = useState(initialRule?.eventType || 'issues.opened');
  const [priority, setPriority] = useState(initialRule?.priority ?? 1);
  const [isEnabled, setIsEnabled] = useState(initialRule?.enabled ?? true);

  const [conditions, setConditions] = useState<RuleCondition[]>(
    initialRule?.conditions?.length
      ? initialRule.conditions.map(c => ({ ...c }))
      : [{ conditionType: 'TitleContains', value: '', caseSensitive: false }]
  );

  const [actions, setActions] = useState<RuleAction[]>(
    initialRule?.actions?.length
      ? initialRule.actions.map(a => ({ ...a }))
      : [{ actionType: 'AddLabel', executionOrder: 1, configuration: { label: '' } }]
  );

  const [formError, setFormError] = useState<string | null>(null);

  const handleAddCondition = () => {
    setConditions([
      ...conditions,
      { conditionType: 'TitleContains', value: '', caseSensitive: false },
    ]);
  };

  const handleRemoveCondition = (index: number) => {
    setConditions(conditions.filter((_, i) => i !== index));
  };

  const handleConditionChange = (
    index: number,
    field: keyof RuleCondition,
    value: any
  ) => {
    const updated = [...conditions];
    updated[index] = { ...updated[index], [field]: value };
    setConditions(updated);
  };

  const handleAddAction = (type: 'AddLabel' | 'AddComment' | 'SlackNotify' | 'AiTriage') => {
    let config: Record<string, any> = {};
    if (type === 'AddLabel') config = { label: '' };
    if (type === 'AddComment') config = { body: '' };
    if (type === 'SlackNotify') config = { message: '🐛 Bug detected in {{repository}}: {{title}}' };
    if (type === 'AiTriage') config = {};
    if (type === 'SlackNotify') config = { message: '🐛 Bug detected in {{repository}}: {{title}}' };

    setActions([
      ...actions,
      {
        actionType: type,
        executionOrder: actions.length + 1,
        configuration: config,
      },
    ]);
  };

  const handleRemoveAction = (index: number) => {
    const updated = actions.filter((_, i) => i !== index);
    // re-index execution order
    updated.forEach((a, i) => {
      a.executionOrder = i + 1;
    });
    setActions(updated);
  };

  const handleActionConfigChange = (index: number, key: string, value: string) => {
    const updated = [...actions];
    updated[index] = {
      ...updated[index],
      configuration: {
        ...updated[index].configuration,
        [key]: value,
      },
    };
    setActions(updated);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    if (!name.trim()) {
      setFormError('Rule name is required.');
      return;
    }

    if (actions.length === 0) {
      setFormError('At least one action is required.');
      return;
    }

    // Validate action configs
    for (const a of actions) {
      if (a.actionType === 'AddLabel' && !a.configuration.label?.trim()) {
        setFormError('Please enter a label name for the Add Label action.');
        return;
      }
      if (a.actionType === 'AddComment' && !a.configuration.body?.trim()) {
        setFormError('Please enter a comment body for the Add Comment action.');
        return;
      }
      if (a.actionType === 'SlackNotify' && !a.configuration.message?.trim()) {
        setFormError('Please enter a message template for the Slack Notification action.');
        return;
      }
    }

    // Filter out empty conditions
    const validConditions = conditions.filter(c => c.value?.trim());

    if (initialRule) {
      onSubmit({
        name: name.trim(),
        eventType: eventType.trim(),
        priority: Number(priority),
        isEnabled,
        conditions: validConditions,
        actions,
      } as UpdateRuleDto);
    } else {
      onSubmit({
        name: name.trim(),
        eventType: eventType.trim(),
        priority: Number(priority),
        conditions: validConditions,
        actions,
      } as CreateRuleDto);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="card" style={{ maxWidth: '800px', margin: '0 auto' }}>
      <h2 style={{ marginBottom: '1.25rem' }}>
        {initialRule ? 'Edit Automation Rule' : 'Create Automation Rule'}
      </h2>

      {formError && (
        <div style={{ background: 'var(--danger-bg)', border: '1px solid var(--danger-border)', color: 'var(--danger-text)', padding: '0.75rem', borderRadius: '0.375rem', marginBottom: '1.25rem', fontSize: '0.875rem' }}>
          {formError}
        </div>
      )}

      {/* Step 1: Rule Name */}
      <div className="form-group">
        <label className="form-label">Step 1: Rule Name</label>
        <input
          type="text"
          className="form-input"
          placeholder="e.g. Bug Issue Alert"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />
      </div>

      {/* Step 2: Event Type */}
      <div className="form-group">
        <label className="form-label">Step 2: Event Type</label>
        <select
          className="form-select"
          value={eventType}
          onChange={(e) => setEventType(e.target.value)}
        >
          <option value="issues.opened">issues.opened — Issue Created</option>
          <option value="pull_request.opened">pull_request.opened — Pull Request Created</option>
          <option value="push">push — Code Pushed</option>
        </select>
      </div>

      {/* Step 3: Conditions */}
      <div className="form-group">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
          <div>
            <label className="form-label" style={{ marginBottom: 0 }}>Step 3: Conditions</label>
            <p style={{ fontSize: '0.75rem' }}>ALL conditions must match for actions to trigger.</p>
          </div>
          <button
            type="button"
            onClick={handleAddCondition}
            className="btn btn-outline btn-sm"
          >
            + Add Condition
          </button>
        </div>

        {conditions.map((cond, idx) => (
          <div
            key={idx}
            style={{
              display: 'flex',
              gap: '0.5rem',
              alignItems: 'center',
              marginBottom: '0.5rem',
              background: 'var(--bg-primary)',
              padding: '0.5rem 0.75rem',
              borderRadius: '0.375rem',
              border: '1px solid var(--border-color)',
            }}
          >
            <select
              className="form-select"
              style={{ width: '180px' }}
              value={cond.conditionType}
              onChange={(e) =>
                handleConditionChange(idx, 'conditionType', e.target.value)
              }
            >
              <option value="TitleContains">Title Contains</option>
              <option value="AuthorEquals">Author Equals</option>
              <option value="LabelContains">Label Contains</option>
            </select>

            <input
              type="text"
              className="form-input"
              style={{ flex: 1 }}
              placeholder="Value to match (e.g. bug, octocat)"
              value={cond.value}
              onChange={(e) => handleConditionChange(idx, 'value', e.target.value)}
            />

            <label style={{ display: 'flex', alignItems: 'center', gap: '0.25rem', fontSize: '0.75rem', color: 'var(--text-secondary)', cursor: 'pointer' }}>
              <input
                type="checkbox"
                checked={cond.caseSensitive || false}
                onChange={(e) => handleConditionChange(idx, 'caseSensitive', e.target.checked)}
              />
              Case-sensitive
            </label>

            <button
              type="button"
              onClick={() => handleRemoveCondition(idx)}
              className="btn btn-danger btn-sm"
            >
              ✕
            </button>
          </div>
        ))}
      </div>

      {/* Step 4: Actions */}
      <div className="form-group">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
          <div>
            <label className="form-label" style={{ marginBottom: 0 }}>Step 4: Actions</label>
            <p style={{ fontSize: '0.75rem' }}>Actions execute sequentially in the background.</p>
          </div>
          <div style={{ display: 'flex', gap: '0.375rem' }}>
            <button
              type="button"
              onClick={() => handleAddAction('AddLabel')}
              className="btn btn-outline btn-sm"
            >
              + Label
            </button>
            <button
              type="button"
              onClick={() => handleAddAction('AddComment')}
              className="btn btn-outline btn-sm"
            >
              + Comment
            </button>
            <button
              type="button"
              onClick={() => handleAddAction('SlackNotify')}
              className="btn btn-outline btn-sm"
            >
              + Slack
            </button>
            <button
              type="button"
              onClick={() => handleAddAction('AiTriage')}
              className="btn btn-outline btn-sm"
              title="Automatically summarize and categorize using AI"
            >
              + AI Triage
            </button>
          </div>
        </div>

        {actions.map((act, idx) => (
          <div
            key={idx}
            style={{
              marginBottom: '0.75rem',
              background: 'var(--bg-primary)',
              padding: '0.75rem',
              borderRadius: '0.375rem',
              border: '1px solid var(--border-color)',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
              <span style={{ fontWeight: 600, fontSize: '0.875rem', color: 'var(--accent-secondary)' }}>
                Action #{act.executionOrder}: {act.actionType}
              </span>
              <button
                type="button"
                onClick={() => handleRemoveAction(idx)}
                className="btn btn-danger btn-sm"
              >
                ✕ Remove
              </button>
            </div>

            {act.actionType === 'AddLabel' && (
              <div>
                <label className="form-label" style={{ fontSize: '0.75rem' }}>Label Name</label>
                <input
                  type="text"
                  className="form-input"
                  placeholder="e.g. bug, triage, urgent"
                  value={act.configuration.label || ''}
                  onChange={(e) => handleActionConfigChange(idx, 'label', e.target.value)}
                  required
                />
              </div>
            )}

            {act.actionType === 'AddComment' && (
              <div>
                <label className="form-label" style={{ fontSize: '0.75rem' }}>Comment Body</label>
                <textarea
                  className="form-textarea"
                  placeholder="e.g. Thanks for opening this issue! We will review it shortly."
                  value={act.configuration.body || ''}
                  onChange={(e) => handleActionConfigChange(idx, 'body', e.target.value)}
                  required
                />
              </div>
            )}

            {act.actionType === 'SlackNotify' && (
              <div>
                <label className="form-label" style={{ fontSize: '0.75rem' }}>Slack Message Template</label>
                <textarea
                  className="form-textarea"
                  placeholder="e.g. 🐛 Bug detected in {{repository}}: {{title}} by {{author}} — {{url}}"
                  value={act.configuration.message || ''}
                  onChange={(e) => handleActionConfigChange(idx, 'message', e.target.value)}
                  required
                />
                <p className="form-hint">
                  Supported variables: <span className="code-tag">{'{{title}}'}</span>, <span className="code-tag">{'{{author}}'}</span>, <span className="code-tag">{'{{action}}'}</span>, <span className="code-tag">{'{{repository}}'}</span>, <span className="code-tag">{'{{event}}'}</span>, <span className="code-tag">{'{{issueNumber}}'}</span>, <span className="code-tag">{'{{url}}'}</span>
                </p>
              </div>
            )}

            {act.actionType === 'AiTriage' && (
              <div>
                <p className="form-hint" style={{ fontSize: '0.75rem', margin: 0 }}>
                  AI Triage will automatically analyze the issue/PR and provide a structured summary, category, and severity in the Activity log.
                </p>
              </div>
            )}
          </div>
        ))}
      </div>

      {/* Step 5: Priority */}
      <div className="form-group" style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
        <div style={{ flex: 1 }}>
          <label className="form-label">Step 5: Execution Priority</label>
          <input
            type="number"
            min="0"
            max="1000"
            className="form-input"
            value={priority}
            onChange={(e) => setPriority(Number(e.target.value))}
          />
          <p className="form-hint">Lower numbers execute first.</p>
        </div>

        {initialRule && (
          <div style={{ flex: 1 }}>
            <label className="form-label">Rule Active Status</label>
            <label style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', cursor: 'pointer', marginTop: '0.5rem' }}>
              <input
                type="checkbox"
                checked={isEnabled}
                onChange={(e) => setIsEnabled(e.target.checked)}
              />
              <span>Enabled</span>
            </label>
          </div>
        )}
      </div>

      <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'flex-end', marginTop: '1.5rem' }}>
        <button
          type="button"
          onClick={onCancel}
          disabled={isProcessing}
          className="btn btn-secondary"
        >
          Cancel
        </button>
        <button
          type="submit"
          disabled={isProcessing}
          className="btn btn-primary"
        >
          {isProcessing ? 'Saving...' : initialRule ? 'Save Changes' : 'Create Rule'}
        </button>
      </div>
    </form>
  );
};
