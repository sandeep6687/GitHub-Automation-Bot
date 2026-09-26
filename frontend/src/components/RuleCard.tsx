import React from 'react';
import type { Rule } from '../types/rule';

interface RuleCardProps {
  rule: Rule;
  onToggle: (ruleId: string, enabled: boolean) => void;
  onEdit: (rule: Rule) => void;
  onDelete: (ruleId: string) => void;
  isProcessing?: boolean;
}

export const RuleCard: React.FC<RuleCardProps> = ({
  rule,
  onToggle,
  onEdit,
  onDelete,
  isProcessing = false,
}) => {
  return (
    <div className="card" style={{ opacity: rule.enabled ? 1 : 0.65 }}>
      <div className="card-header">
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
          <label className="switch">
            <input
              type="checkbox"
              checked={rule.enabled}
              onChange={(e) => onToggle(rule.id, e.target.checked)}
              disabled={isProcessing}
            />
            <span className="slider"></span>
          </label>
          <div>
            <h3 style={{ fontSize: '1.125rem', marginBottom: '0.25rem' }}>{rule.name}</h3>
            <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
              <span className="code-tag">{rule.eventType}</span>
              <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                Priority: {rule.priority}
              </span>
            </div>
          </div>
        </div>

        <div style={{ display: 'flex', gap: '0.5rem' }}>
          <button
            onClick={() => onEdit(rule)}
            className="btn btn-outline btn-sm"
            disabled={isProcessing}
          >
            Edit
          </button>
          <button
            onClick={() => onDelete(rule.id)}
            className="btn btn-danger btn-sm"
            disabled={isProcessing}
          >
            Delete
          </button>
        </div>
      </div>

      <div style={{ marginTop: '1rem', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
        {/* Conditions */}
        <div style={{ background: 'var(--bg-primary)', padding: '0.75rem', borderRadius: '0.375rem', border: '1px solid var(--border-color)' }}>
          <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase' }}>
            Conditions ({rule.conditions.length === 0 ? 'None — Matches All' : 'ALL Must Match'})
          </span>
          {rule.conditions.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.375rem', marginTop: '0.375rem' }}>
              {rule.conditions.map((c, i) => (
                <div key={i} style={{ fontSize: '0.8125rem' }}>
                  • <span style={{ color: 'var(--text-secondary)' }}>{c.conditionType}:</span>{' '}
                  <span className="code-tag">{c.value}</span>
                  {c.caseSensitive && <span style={{ fontSize: '0.75rem', color: 'var(--warning-text)', marginLeft: '0.5rem' }}>(case-sensitive)</span>}
                </div>
              ))}
            </div>
          ) : (
            <p style={{ fontSize: '0.8125rem', marginTop: '0.25rem' }}>Matches all incoming {rule.eventType} events.</p>
          )}
        </div>

        {/* Actions */}
        <div style={{ background: 'var(--bg-primary)', padding: '0.75rem', borderRadius: '0.375rem', border: '1px solid var(--border-color)' }}>
          <span style={{ fontSize: '0.75rem', fontWeight: 600, color: 'var(--text-muted)', textTransform: 'uppercase' }}>
            Actions ({rule.actions.length})
          </span>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.375rem', marginTop: '0.375rem' }}>
            {rule.actions.map((a, i) => (
              <div key={i} style={{ fontSize: '0.8125rem' }}>
                <span style={{ fontWeight: 600, color: 'var(--accent-secondary)' }}>
                  #{a.executionOrder} {a.actionType}:
                </span>{' '}
                {a.actionType === 'AddLabel' && (
                  <span>Label: <span className="code-tag">{a.configuration.label}</span></span>
                )}
                {a.actionType === 'AddComment' && (
                  <span>Comment: <em>"{a.configuration.body}"</em></span>
                )}
                {a.actionType === 'SlackNotify' && (
                  <span>Slack Message: <em>"{a.configuration.message}"</em></span>
                )}
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
