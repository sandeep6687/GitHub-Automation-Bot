import React, { useEffect, useState } from 'react';
import type { ConnectedRepository } from '../types/repository';
import { repositoryApi } from '../api/repositoryApi';
import { ruleApi } from '../api/ruleApi';
import { activityApi } from '../api/activityApi';
import { LoadingState } from '../components/LoadingState';

interface DashboardPageProps {
  onNavigate: (tab: 'dashboard' | 'repositories' | 'rules' | 'activity') => void;
}

export const DashboardPage: React.FC<DashboardPageProps> = ({ onNavigate }) => {
  const [loading, setLoading] = useState(true);
  const [connectedRepos, setConnectedRepos] = useState<ConnectedRepository[]>([]);
  const [activeRulesCount, setActiveRulesCount] = useState<number>(0);
  const [recentEventsCount, setRecentEventsCount] = useState<number>(0);
  const [failedEventsCount, setFailedEventsCount] = useState<number>(0);

  useEffect(() => {
    loadDashboardMetrics();
  }, []);

  const loadDashboardMetrics = async () => {
    setLoading(true);
    try {
      const repos = await repositoryApi.getConnectedRepositories();
      setConnectedRepos(repos);

      let totalActiveRules = 0;
      let totalRecentEvents = 0;
      let totalFailedEvents = 0;

      for (const repo of repos) {
        try {
          const rules = await ruleApi.getRules(repo.id);
          totalActiveRules += rules.filter(r => r.enabled).length;

          const activity = await activityApi.getActivity(repo.id, { limit: 50 });
          totalRecentEvents += activity.items.length;
          totalFailedEvents += activity.items.filter(e => e.status === 'Failed' || e.status === 'Retrying').length;
        } catch {
          // Non-blocking per repository
        }
      }

      setActiveRulesCount(totalActiveRules);
      setRecentEventsCount(totalRecentEvents);
      setFailedEventsCount(totalFailedEvents);
    } catch (err) {
      console.error('Failed to load dashboard data:', err);
    } finally {
      setLoading(false);
    }
  };

  if (loading) {
    return <LoadingState message="Loading dashboard metrics..." />;
  }

  return (
    <div>
      <div style={{ marginBottom: '2rem' }}>
        <h1 style={{ fontSize: '1.75rem', marginBottom: '0.5rem' }}>Dashboard Overview</h1>
        <p>Monitor your connected repositories, automation rules, and live event deliveries.</p>
      </div>

      {/* Summary Cards */}
      <div className="grid-4" style={{ marginBottom: '2rem' }}>
        <div className="card" style={{ cursor: 'pointer' }} onClick={() => onNavigate('repositories')}>
          <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 600 }}>
            Connected Repositories
          </div>
          <div style={{ fontSize: '2.25rem', fontWeight: 700, margin: '0.5rem 0', color: 'var(--text-primary)' }}>
            {connectedRepos.length}
          </div>
          <span style={{ fontSize: '0.8125rem', color: 'var(--accent-secondary)' }}>
            Manage Repositories →
          </span>
        </div>

        <div className="card" style={{ cursor: 'pointer' }} onClick={() => onNavigate('rules')}>
          <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 600 }}>
            Active Rules
          </div>
          <div style={{ fontSize: '2.25rem', fontWeight: 700, margin: '0.5rem 0', color: 'var(--success-text)' }}>
            {activeRulesCount}
          </div>
          <span style={{ fontSize: '0.8125rem', color: 'var(--accent-secondary)' }}>
            View Automation Rules →
          </span>
        </div>

        <div className="card" style={{ cursor: 'pointer' }} onClick={() => onNavigate('activity')}>
          <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 600 }}>
            Recent Events
          </div>
          <div style={{ fontSize: '2.25rem', fontWeight: 700, margin: '0.5rem 0', color: 'var(--text-primary)' }}>
            {recentEventsCount}
          </div>
          <span style={{ fontSize: '0.8125rem', color: 'var(--accent-secondary)' }}>
            Inspect Event Activity →
          </span>
        </div>

        <div className="card" style={{ cursor: 'pointer' }} onClick={() => onNavigate('activity')}>
          <div style={{ fontSize: '0.8125rem', color: 'var(--text-muted)', textTransform: 'uppercase', fontWeight: 600 }}>
            Failed / Retrying
          </div>
          <div style={{ fontSize: '2.25rem', fontWeight: 700, margin: '0.5rem 0', color: failedEventsCount > 0 ? 'var(--danger-text)' : 'var(--text-muted)' }}>
            {failedEventsCount}
          </div>
          <span style={{ fontSize: '0.8125rem', color: 'var(--accent-secondary)' }}>
            View Failures →
          </span>
        </div>
      </div>

      {/* Quick Launch / Status */}
      <div className="card">
        <h2 style={{ fontSize: '1.25rem', marginBottom: '1rem' }}>Get Started</h2>
        <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
          <button
            onClick={() => onNavigate('repositories')}
            className="btn btn-primary"
          >
            + Connect a Repository
          </button>
          <button
            onClick={() => onNavigate('rules')}
            className="btn btn-secondary"
            disabled={connectedRepos.length === 0}
          >
            Create Automation Rule
          </button>
          <button
            onClick={() => onNavigate('activity')}
            className="btn btn-outline"
          >
            View Live Activity Log
          </button>
        </div>
      </div>
    </div>
  );
};
