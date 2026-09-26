import React from 'react';
import type { UserProfile } from '../types/auth';

interface LayoutProps {
  user: UserProfile | null;
  activeTab: 'dashboard' | 'repositories' | 'rules' | 'activity';
  onTabChange: (tab: 'dashboard' | 'repositories' | 'rules' | 'activity') => void;
  onLogout: () => void;
  children: React.ReactNode;
}

export const Layout: React.FC<LayoutProps> = ({
  user,
  activeTab,
  onTabChange,
  onLogout,
  children,
}) => {
  return (
    <div>
      <header className="navbar">
        <div className="app-container navbar-inner">
          <div className="brand" style={{ cursor: 'pointer' }} onClick={() => onTabChange('dashboard')}>
            <span className="brand-icon">⚡</span>
            <span>Abstrabit Bot</span>
          </div>

          {user && (
            <nav className="nav-links">
              <button
                className={`nav-link ${activeTab === 'dashboard' ? 'active' : ''}`}
                onClick={() => onTabChange('dashboard')}
              >
                Dashboard
              </button>
              <button
                className={`nav-link ${activeTab === 'repositories' ? 'active' : ''}`}
                onClick={() => onTabChange('repositories')}
              >
                Repositories
              </button>
              <button
                className={`nav-link ${activeTab === 'rules' ? 'active' : ''}`}
                onClick={() => onTabChange('rules')}
              >
                Rules
              </button>
              <button
                className={`nav-link ${activeTab === 'activity' ? 'active' : ''}`}
                onClick={() => onTabChange('activity')}
              >
                Activity
              </button>
            </nav>
          )}

          {user && (
            <div className="user-profile">
              {user.avatarUrl && (
                <img src={user.avatarUrl} alt={user.login} className="avatar" />
              )}
              <span style={{ fontSize: '0.875rem', fontWeight: 600 }}>{user.login}</span>
              <button onClick={onLogout} className="btn btn-outline btn-sm">
                Sign Out
              </button>
            </div>
          )}
        </div>
      </header>

      <main className="main-content">
        <div className="app-container">{children}</div>
      </main>
    </div>
  );
};
