import React from 'react';
import type { ConnectedRepository, AvailableRepository } from '../types/repository';

interface RepositoryCardProps {
  repo: ConnectedRepository | AvailableRepository;
  isConnected: boolean;
  onConnect?: (githubRepoId: number) => void;
  onDisconnect?: (id: string) => void;
  onViewRules?: (repoId: string) => void;
  onViewActivity?: (repoId: string) => void;
  isProcessing?: boolean;
}

export const RepositoryCard: React.FC<RepositoryCardProps> = ({
  repo,
  isConnected,
  onConnect,
  onDisconnect,
  onViewRules,
  onViewActivity,
  isProcessing = false,
}) => {
  if (isConnected) {
    const connectedRepo = repo as ConnectedRepository;
    return (
      <div className="card">
        <div className="card-header">
          <div>
            <h3 style={{ fontSize: '1.125rem', marginBottom: '0.25rem' }}>{connectedRepo.fullName}</h3>
            <p style={{ fontSize: '0.75rem' }}>
              Branch: <span className="code-tag">{connectedRepo.defaultBranch}</span> · Connected:{' '}
              {new Date(connectedRepo.createdAt).toLocaleDateString()}
            </p>
          </div>
          <span className="badge badge-success">Connected</span>
        </div>

        {(!connectedRepo.installationId) && (
          <div style={{
            marginTop: '1rem',
            padding: '0.5rem',
            backgroundColor: 'var(--warning-bg, #fff3cd)',
            color: 'var(--warning-text, #856404)',
            borderRadius: '0.25rem',
            fontSize: '0.75rem',
            border: '1px solid var(--warning-border, #ffeeba)'
          }}>
            <strong>Note:</strong> The GitHub App is not installed for this repository. Bot actions will use your personal OAuth token (fallback mode).<br />
            <a 
              href="https://github.com/settings/apps" 
              target="_blank" 
              rel="noopener noreferrer"
              style={{ color: 'inherit', textDecoration: 'underline', fontWeight: 'bold' }}
            >
              Install GitHub App
            </a> to improve reliability and security.
          </div>
        )}

        <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', marginTop: '1rem' }}>
          {onViewRules && (
            <button
              onClick={() => onViewRules(connectedRepo.id)}
              className="btn btn-secondary btn-sm"
            >
              View Rules
            </button>
          )}
          {onViewActivity && (
            <button
              onClick={() => onViewActivity(connectedRepo.id)}
              className="btn btn-secondary btn-sm"
            >
              View Activity
            </button>
          )}
          {onDisconnect && (
            <button
              onClick={() => onDisconnect(connectedRepo.id)}
              disabled={isProcessing}
              className="btn btn-danger btn-sm"
              style={{ marginLeft: 'auto' }}
            >
              {isProcessing ? 'Disconnecting...' : 'Disconnect'}
            </button>
          )}
        </div>
      </div>
    );
  }

  const availableRepo = repo as AvailableRepository;
  return (
    <div className="card">
      <div className="card-header">
        <div>
          <h3 style={{ fontSize: '1rem', marginBottom: '0.25rem' }}>{availableRepo.fullName}</h3>
          <p style={{ fontSize: '0.75rem' }}>
            Branch: <span className="code-tag">{availableRepo.defaultBranch}</span>
          </p>
        </div>
        {availableRepo.isConnected ? (
          <span className="badge badge-success">Already Added</span>
        ) : (
          onConnect && (
            <button
              onClick={() => onConnect(availableRepo.id)}
              disabled={isProcessing}
              className="btn btn-primary btn-sm"
            >
              {isProcessing ? 'Connecting...' : 'Connect'}
            </button>
          )
        )}
      </div>
    </div>
  );
};
