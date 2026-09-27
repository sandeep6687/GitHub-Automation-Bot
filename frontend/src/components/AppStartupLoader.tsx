import React from 'react';

interface AppStartupLoaderProps {
  error?: string | null;
  onRetry?: () => void;
}

export const AppStartupLoader: React.FC<AppStartupLoaderProps> = ({ error, onRetry }) => {
  if (error) {
    return (
      <div className="startup-loader">
        <div className="startup-loader__error-icon">⚠️</div>
        <div className="startup-loader__error-title">Unable to connect</div>
        <div className="startup-loader__error-body">Please try again.</div>
        {onRetry && (
          <button onClick={onRetry} className="btn btn-outline">
            Retry
          </button>
        )}
      </div>
    );
  }

  return (
    <div className="startup-loader">
      <div className="startup-loader__icon">⚡</div>
      <div className="startup-loader__brand">Abstrabit Bot</div>
      <div className="startup-loader__subtitle">Event-driven GitHub automation</div>
      <div className="startup-loader__dots">
        <div className="startup-loader__dot" />
        <div className="startup-loader__dot" />
        <div className="startup-loader__dot" />
      </div>
    </div>
  );
};
