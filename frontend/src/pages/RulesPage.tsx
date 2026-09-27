import React, { useEffect, useState, useRef } from 'react';
import type { ConnectedRepository } from '../types/repository';
import type { Rule, CreateRuleDto, UpdateRuleDto } from '../types/rule';
import { repositoryApi } from '../api/repositoryApi';
import { ruleApi } from '../api/ruleApi';
import { RuleCard } from '../components/RuleCard';
import { RuleForm } from '../components/RuleForm';
import { RulesSkeleton, PageErrorState } from '../components/Skeleton';

interface RulesPageProps {
  initialRepoId?: string | null;
}

export const RulesPage: React.FC<RulesPageProps> = ({ initialRepoId }) => {
  const [repos, setRepos] = useState<ConnectedRepository[]>([]);
  const [selectedRepoId, setSelectedRepoId] = useState<string>(initialRepoId || '');
  const [rules, setRules] = useState<Rule[]>([]);
  const [loading, setLoading] = useState(true);
  const [pageError, setPageError] = useState(false);
  const [isCreatingOrEditing, setIsCreatingOrEditing] = useState(false);
  const [editingRule, setEditingRule] = useState<Rule | null>(null);
  const [processingId, setProcessingId] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ message: string; type: 'success' | 'error' } | null>(null);
  const feedbackRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (feedback && feedbackRef.current) {
      feedbackRef.current.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
  }, [feedback]);

  useEffect(() => {
    loadRepositories();
  }, []);

  useEffect(() => {
    if (selectedRepoId) {
      loadRules(selectedRepoId);
    } else {
      setRules([]);
    }
  }, [selectedRepoId]);

  const loadRepositories = async () => {
    setLoading(true);
    setPageError(false);
    try {
      const data = await repositoryApi.getConnectedRepositories();
      setRepos(data);
      if (!selectedRepoId && data.length > 0) {
        setSelectedRepoId(data[0].id);
      } else if (data.length === 0) {
        setLoading(false); // No repos, stop loading to show "No Repos" message
      }
    } catch (err: any) {
      setPageError(true);
      setLoading(false);
    }
  };

  const loadRules = async (repoId: string) => {
    setLoading(true);
    setPageError(false);
    setFeedback(null);
    try {
      const data = await ruleApi.getRules(repoId);
      setRules(data);
    } catch (err: any) {
      setPageError(true);
    } finally {
      setLoading(false);
    }
  };

  const handleRetry = () => {
    if (!repos.length) {
      loadRepositories();
    } else if (selectedRepoId) {
      loadRules(selectedRepoId);
    }
  };

  const handleToggle = async (ruleId: string, enabled: boolean) => {
    if (!selectedRepoId) return;
    setProcessingId(ruleId);
    try {
      const updated = await ruleApi.toggleRule(selectedRepoId, ruleId, enabled);
      setRules(rules.map((r) => (r.id === ruleId ? updated : r)));
    } catch (err: any) {
      setFeedback({ message: err.message || 'Failed to toggle rule', type: 'error' });
    } finally {
      setProcessingId(null);
    }
  };

  const handleDelete = async (ruleId: string) => {
    if (!selectedRepoId || !window.confirm('Are you sure you want to delete this rule?')) return;
    setProcessingId(ruleId);
    try {
      await ruleApi.deleteRule(selectedRepoId, ruleId);
      setRules(rules.filter((r) => r.id !== ruleId));
      setFeedback({ message: 'Rule deleted successfully.', type: 'success' });
    } catch (err: any) {
      setFeedback({ message: err.message || 'Failed to delete rule', type: 'error' });
    } finally {
      setProcessingId(null);
    }
  };

  const handleFormSubmit = async (data: CreateRuleDto | UpdateRuleDto) => {
    if (!selectedRepoId) return;
    setFeedback(null);
    try {
      if (editingRule) {
        const updated = await ruleApi.updateRule(selectedRepoId, editingRule.id, data as UpdateRuleDto);
        setRules(rules.map((r) => (r.id === editingRule.id ? updated : r)));
        setFeedback({ message: 'Rule updated successfully.', type: 'success' });
      } else {
        const created = await ruleApi.createRule(selectedRepoId, data as CreateRuleDto);
        setRules([...rules, created]);
        setFeedback({ message: 'Rule created successfully.', type: 'success' });
      }
      setIsCreatingOrEditing(false);
      setEditingRule(null);
    } catch (err: any) {
      setFeedback({ message: err.message || 'Failed to save rule', type: 'error' });
    }
  };

  if (loading) {
    return <RulesSkeleton />;
  }

  if (pageError) {
    return <PageErrorState onRetry={handleRetry} />;
  }

  if (repos.length === 0) {
    return (
      <div className="card" style={{ textAlign: 'center', padding: '3rem' }}>
        <h2 style={{ marginBottom: '1rem' }}>No Repositories Connected</h2>
        <p style={{ marginBottom: '1.5rem' }}>
          You must connect at least one GitHub repository before defining automation rules.
        </p>
      </div>
    );
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '2rem', flexWrap: 'wrap', gap: '1rem' }}>
        <div>
          <h1 style={{ fontSize: '1.75rem', marginBottom: '0.5rem' }}>Automation Rules</h1>
          <p>Configure condition-action automation pipelines triggered by webhook events.</p>
        </div>

        {!isCreatingOrEditing && (
          <button
            onClick={() => {
              setEditingRule(null);
              setIsCreatingOrEditing(true);
            }}
            className="btn btn-primary"
          >
            + Create New Rule
          </button>
        )}
      </div>

      {feedback && (
        <div
          ref={feedbackRef}
          style={{
            padding: '0.875rem 1rem',
            borderRadius: '0.375rem',
            marginBottom: '1.5rem',
            fontSize: '0.875rem',
            backgroundColor: feedback.type === 'success' ? 'var(--success-bg)' : 'var(--danger-bg)',
            color: feedback.type === 'success' ? 'var(--success-text)' : 'var(--danger-text)',
            border: `1px solid ${feedback.type === 'success' ? 'var(--success-border)' : 'var(--danger-border)'}`,
          }}
        >
          {feedback.message}
        </div>
      )}

      {/* Repository Selector */}
      <div className="card" style={{ padding: '1rem 1.25rem', marginBottom: '1.5rem' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '1rem', flexWrap: 'wrap' }}>
          <label style={{ fontWeight: 600, fontSize: '0.875rem' }}>Active Repository:</label>
          <select
            className="form-select"
            style={{ maxWidth: '320px' }}
            value={selectedRepoId}
            onChange={(e) => setSelectedRepoId(e.target.value)}
            disabled={isCreatingOrEditing}
          >
            {repos.map((r) => (
              <option key={r.id} value={r.id}>
                {r.fullName}
              </option>
            ))}
          </select>
          <span style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
            ({rules.length} rule{rules.length === 1 ? '' : 's'} configured)
          </span>
        </div>
      </div>

      {/* Form View or Cards List */}
      {isCreatingOrEditing ? (
        <RuleForm
          initialRule={editingRule}
          onSubmit={handleFormSubmit}
          onCancel={() => {
            setIsCreatingOrEditing(false);
            setEditingRule(null);
          }}
        />
      ) : (
        <div>
          {loading ? (
            <LoadingState message="Loading rules..." />
          ) : rules.length === 0 ? (
            <div className="card" style={{ textAlign: 'center', padding: '3rem' }}>
              <h3 style={{ marginBottom: '0.5rem' }}>No Rules Defined Yet</h3>
              <p style={{ marginBottom: '1.5rem', fontSize: '0.875rem' }}>
                Add your first rule to automatically label issues, post comments, or send Slack notifications.
              </p>
              <button
                onClick={() => {
                  setEditingRule(null);
                  setIsCreatingOrEditing(true);
                }}
                className="btn btn-primary"
              >
                Create Rule
              </button>
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
              {rules.map((rule) => (
                <RuleCard
                  key={rule.id}
                  rule={rule}
                  onToggle={handleToggle}
                  onEdit={(r) => {
                    setEditingRule(r);
                    setIsCreatingOrEditing(true);
                  }}
                  onDelete={handleDelete}
                  isProcessing={processingId === rule.id}
                />
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
