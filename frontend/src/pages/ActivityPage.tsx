import React, { useEffect, useState, useRef, useCallback } from 'react';
import type { ConnectedRepository } from '../types/repository';
import type { ActivityEvent } from '../types/activity';
import { repositoryApi } from '../api/repositoryApi';
import { activityApi } from '../api/activityApi';
import { ActivityTable } from '../components/ActivityTable';
import { LoadingState } from '../components/LoadingState';

interface ActivityPageProps {
  initialRepoId?: string | null;
}

export const ActivityPage: React.FC<ActivityPageProps> = ({ initialRepoId }) => {
  const [repos, setRepos] = useState<ConnectedRepository[]>([]);
  const [selectedRepoId, setSelectedRepoId] = useState<string>(initialRepoId || '');
  const [events, setEvents] = useState<ActivityEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [eventTypeFilter, setEventTypeFilter] = useState<string>('');
  const [autoRefresh, setAutoRefresh] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  const pollingTimerRef = useRef<number | null>(null);

  useEffect(() => {
    loadRepositories();
  }, []);

  const loadRepositories = async () => {
    try {
      const data = await repositoryApi.getConnectedRepositories();
      setRepos(data);
      if (!selectedRepoId && data.length > 0) {
        setSelectedRepoId(data[0].id);
      }
    } catch (err: any) {
      setError(err.message || 'Failed to load repositories');
      setLoading(false);
    }
  };

  const fetchActivity = useCallback(async (isBackground = false) => {
    if (!selectedRepoId) return;
    if (isBackground) {
      setIsRefreshing(true);
    } else {
      setLoading(true);
    }
    setError(null);

    try {
      const res = await activityApi.getActivity(selectedRepoId, {
        limit: 50,
        status: statusFilter || undefined,
        eventType: eventTypeFilter || undefined,
      });
      setEvents(res.items);
    } catch (err: any) {
      setError(err.message || 'Failed to load activity');
    } finally {
      setLoading(false);
      setIsRefreshing(false);
    }
  }, [selectedRepoId, statusFilter, eventTypeFilter]);

  // Initial fetch when repo or filters change
  useEffect(() => {
    if (selectedRepoId) {
      fetchActivity(false);
    } else {
      setEvents([]);
      setLoading(false);
    }
  }, [selectedRepoId, statusFilter, eventTypeFilter, fetchActivity]);

  // 6-second polling setup with cleanup
  useEffect(() => {
    if (!autoRefresh || !selectedRepoId) {
      if (pollingTimerRef.current) {
        window.clearInterval(pollingTimerRef.current);
        pollingTimerRef.current = null;
      }
      return;
    }

    pollingTimerRef.current = window.setInterval(() => {
      fetchActivity(true);
    }, 6000);

    return () => {
      if (pollingTimerRef.current) {
        window.clearInterval(pollingTimerRef.current);
        pollingTimerRef.current = null;
      }
    };
  }, [autoRefresh, selectedRepoId, fetchActivity]);

  if (loading && repos.length === 0) {
    return <LoadingState message="Loading activity history..." />;
  }

  if (repos.length === 0) {
    return (
      <div className="card" style={{ textAlign: 'center', padding: '3rem' }}>
        <h2 style={{ marginBottom: '1rem' }}>No Repositories Connected</h2>
        <p>Connect a GitHub repository to view incoming webhook events and action executions.</p>
      </div>
    );
  }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '2rem', flexWrap: 'wrap', gap: '1rem' }}>
        <div>
          <h1 style={{ fontSize: '1.75rem', marginBottom: '0.5rem' }}>Webhook Activity Log</h1>
          <p>Real-time delivery status, rule evaluations, and action execution outcomes.</p>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: '0.375rem', fontSize: '0.8125rem', color: 'var(--text-secondary)', cursor: 'pointer' }}>
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
            />
            Auto-refresh (6s)
          </label>
        </div>
      </div>

      {error && (
        <div style={{ background: 'var(--danger-bg)', border: '1px solid var(--danger-border)', color: 'var(--danger-text)', padding: '0.875rem 1rem', borderRadius: '0.375rem', marginBottom: '1.5rem', fontSize: '0.875rem' }}>
          {error}
        </div>
      )}

      {/* Filters Bar */}
      <div className="card" style={{ padding: '1rem 1.25rem', marginBottom: '1.5rem' }}>
        <div style={{ display: 'flex', gap: '1.25rem', flexWrap: 'wrap', alignItems: 'center' }}>
          <div>
            <label className="form-label" style={{ fontSize: '0.75rem', marginBottom: '0.25rem' }}>Repository</label>
            <select
              className="form-select"
              style={{ minWidth: '220px' }}
              value={selectedRepoId}
              onChange={(e) => setSelectedRepoId(e.target.value)}
            >
              {repos.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.fullName}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="form-label" style={{ fontSize: '0.75rem', marginBottom: '0.25rem' }}>Status Filter</label>
            <select
              className="form-select"
              style={{ minWidth: '150px' }}
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
            >
              <option value="">All Statuses</option>
              <option value="Success">Success</option>
              <option value="Retrying">Retrying</option>
              <option value="Failed">Failed</option>
              <option value="Processing">Processing</option>
              <option value="Pending">Pending</option>
            </select>
          </div>

          <div>
            <label className="form-label" style={{ fontSize: '0.75rem', marginBottom: '0.25rem' }}>Event Type</label>
            <select
              className="form-select"
              style={{ minWidth: '150px' }}
              value={eventTypeFilter}
              onChange={(e) => setEventTypeFilter(e.target.value)}
            >
              <option value="">All Events</option>
              <option value="issues">issues</option>
              <option value="pull_request">pull_request</option>
              <option value="push">push</option>
            </select>
          </div>
        </div>
      </div>

      {loading ? (
        <LoadingState message="Fetching events..." />
      ) : (
        <ActivityTable
          events={events}
          onRefresh={() => fetchActivity(true)}
          isRefreshing={isRefreshing}
        />
      )}
    </div>
  );
};
