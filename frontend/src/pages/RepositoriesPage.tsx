import React, { useEffect, useState } from 'react';
import type { ConnectedRepository, AvailableRepository } from '../types/repository';
import { repositoryApi } from '../api/repositoryApi';
import { RepositoryCard } from '../components/RepositoryCard';
import { RepositoriesSkeleton, PageErrorState } from '../components/Skeleton';

interface RepositoriesPageProps {
  onSelectRepoForRules: (repoId: string) => void;
  onSelectRepoForActivity: (repoId: string) => void;
}

export const RepositoriesPage: React.FC<RepositoriesPageProps> = ({
  onSelectRepoForRules,
  onSelectRepoForActivity,
}) => {
  const [loading, setLoading] = useState(true);
  const [pageError, setPageError] = useState(false);
  const [connected, setConnected] = useState<ConnectedRepository[]>([]);
  const [available, setAvailable] = useState<AvailableRepository[]>([]);
  const [processingId, setProcessingId] = useState<string | number | null>(null);
  const [feedback, setFeedback] = useState<{ message: string; type: 'success' | 'error' } | null>(null);

  useEffect(() => {
    loadRepositories();
  }, []);

  const loadRepositories = async () => {
    setLoading(true);
    setPageError(false);
    setFeedback(null);
    try {
      const [connectedData, availableData] = await Promise.all([
        repositoryApi.getConnectedRepositories(),
        repositoryApi.getAvailableRepositories(),
      ]);
      setConnected(connectedData);
      setAvailable(availableData);
    } catch (err: any) {
      setPageError(true);
    } finally {
      setLoading(false);
    }
  };

  const handleConnect = async (githubRepoId: number) => {
    setProcessingId(githubRepoId);
    setFeedback(null);
    try {
      const newRepo = await repositoryApi.connectRepository(githubRepoId);
      setFeedback({
        message: `Successfully connected ${newRepo.fullName}! Webhook registered with GitHub.`,
        type: 'success',
      });
      // Silent reload behind the scenes to avoid full page skeleton
      const [connectedData, availableData] = await Promise.all([
        repositoryApi.getConnectedRepositories(),
        repositoryApi.getAvailableRepositories(),
      ]);
      setConnected(connectedData);
      setAvailable(availableData);
    } catch (err: any) {
      setFeedback({ message: err.message || 'Failed to connect repository', type: 'error' });
    } finally {
      setProcessingId(null);
    }
  };

  const handleDisconnect = async (id: string) => {
    if (!window.confirm('Are you sure you want to disconnect this repository? All rules and webhook deliveries will remain in history.')) {
      return;
    }

    setProcessingId(id);
    setFeedback(null);
    try {
      await repositoryApi.disconnectRepository(id);
      setFeedback({ message: 'Repository disconnected successfully.', type: 'success' });
      // Silent reload behind the scenes
      const [connectedData, availableData] = await Promise.all([
        repositoryApi.getConnectedRepositories(),
        repositoryApi.getAvailableRepositories(),
      ]);
      setConnected(connectedData);
      setAvailable(availableData);
    } catch (err: any) {
      setFeedback({ message: err.message || 'Failed to disconnect repository', type: 'error' });
    } finally {
      setProcessingId(null);
    }
  };

  if (loading) {
    return <RepositoriesSkeleton />;
  }

  if (pageError) {
    return <PageErrorState onRetry={loadRepositories} />;
  }

  const unconnectedAvailable = available.filter(
    (a) => !connected.some((c) => c.githubRepositoryId === a.id)
  );

  return (
    <div>
      <div style={{ marginBottom: '2rem' }}>
        <h1 style={{ fontSize: '1.75rem', marginBottom: '0.5rem' }}>GitHub Repositories</h1>
        <p>Connect your GitHub repositories to start automating actions and alerts on webhook events.</p>
      </div>

      {feedback && (
        <div
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

      {/* Connected Repositories Section */}
      <section style={{ marginBottom: '3rem' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '1rem' }}>
          <h2>Connected Repositories ({connected.length})</h2>
        </div>

        {connected.length === 0 ? (
          <div className="card" style={{ textAlign: 'center', padding: '2.5rem' }}>
            <p style={{ marginBottom: '1rem' }}>No repositories connected yet.</p>
            <p style={{ fontSize: '0.875rem', color: 'var(--text-muted)' }}>
              Choose a repository from the list below to connect and configure automation rules.
            </p>
          </div>
        ) : (
          <div className="grid-2">
            {connected.map((repo) => (
              <RepositoryCard
                key={repo.id}
                repo={repo}
                isConnected={true}
                onDisconnect={handleDisconnect}
                onViewRules={onSelectRepoForRules}
                onViewActivity={onSelectRepoForActivity}
                isProcessing={processingId === repo.id}
              />
            ))}
          </div>
        )}
      </section>

      {/* Available Repositories Section */}
      <section>
        <div style={{ marginBottom: '1rem' }}>
          <h2>Available GitHub Repositories ({unconnectedAvailable.length})</h2>
          <p style={{ fontSize: '0.875rem' }}>Repositories you have admin/webhook permissions for on GitHub.</p>
        </div>

        {unconnectedAvailable.length === 0 ? (
          <div className="card" style={{ textAlign: 'center', padding: '2rem' }}>
            <p>All authorized repositories are already connected!</p>
          </div>
        ) : (
          <div className="grid-3">
            {unconnectedAvailable.map((repo) => (
              <RepositoryCard
                key={repo.id}
                repo={repo}
                isConnected={false}
                onConnect={handleConnect}
                isProcessing={processingId === repo.id}
              />
            ))}
          </div>
        )}
      </section>
    </div>
  );
};
