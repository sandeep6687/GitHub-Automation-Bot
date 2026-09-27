import { useEffect, useState } from 'react';
import type { UserProfile } from './types/auth';
import { authApi } from './api/authApi';
import { Layout } from './components/Layout';
import { AppStartupLoader } from './components/AppStartupLoader';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { RepositoriesPage } from './pages/RepositoriesPage';
import { RulesPage } from './pages/RulesPage';
import { ActivityPage } from './pages/ActivityPage';

export function App() {
  const [user, setUser] = useState<UserProfile | null>(null);
  const [checkingAuth, setCheckingAuth] = useState(true);
  const [authError, setAuthError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'dashboard' | 'repositories' | 'rules' | 'activity'>('dashboard');
  const [activeRepoId, setActiveRepoId] = useState<string | null>(null);

  useEffect(() => {
    checkAuthentication();
  }, []);

  const checkAuthentication = async () => {
    setCheckingAuth(true);
    setAuthError(null);
    try {
      const profile = await authApi.getCurrentUser();
      setUser(profile);
    } catch (err: any) {
      // 401 means unauthenticated, which is expected for logged out users.
      // Other errors might mean API is down.
      if (err.message && !err.message.includes('401')) {
        setAuthError(err.message);
      }
      setUser(null);
    } finally {
      setCheckingAuth(false);
    }
  };

  const handleLogout = async () => {
    try {
      await authApi.logout();
    } catch {
      // Ignore
    } finally {
      setUser(null);
      setActiveTab('dashboard');
    }
  };

  const handleViewRulesForRepo = (repoId: string) => {
    setActiveRepoId(repoId);
    setActiveTab('rules');
  };

  const handleViewActivityForRepo = (repoId: string) => {
    setActiveRepoId(repoId);
    setActiveTab('activity');
  };

  if (checkingAuth) {
    return <AppStartupLoader />;
  }

  if (authError) {
    return <AppStartupLoader error={authError} onRetry={checkAuthentication} />;
  }

  if (!user) {
    return <LoginPage />;
  }

  return (
    <Layout
      user={user}
      activeTab={activeTab}
      onTabChange={(tab) => {
        setActiveTab(tab);
      }}
      onLogout={handleLogout}
    >
      {activeTab === 'dashboard' && (
        <DashboardPage onNavigate={(tab) => setActiveTab(tab)} />
      )}
      {activeTab === 'repositories' && (
        <RepositoriesPage
          onSelectRepoForRules={handleViewRulesForRepo}
          onSelectRepoForActivity={handleViewActivityForRepo}
        />
      )}
      {activeTab === 'rules' && (
        <RulesPage initialRepoId={activeRepoId} />
      )}
      {activeTab === 'activity' && (
        <ActivityPage initialRepoId={activeRepoId} />
      )}
    </Layout>
  );
}

export default App;
