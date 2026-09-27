import React from 'react';

interface SkeletonProps {
  className?: string;
  style?: React.CSSProperties;
}

export const Skeleton: React.FC<SkeletonProps> = ({ className = '', style }) => {
  return (
    <div className={`skeleton ${className}`} style={style} />
  );
};

export const DashboardSkeleton: React.FC = () => (
  <div>
    <div style={{ marginBottom: '2rem' }}>
      <Skeleton className="skeleton-text" style={{ width: '250px', height: '1.75rem', marginBottom: '0.5rem' }} />
      <Skeleton className="skeleton-text" style={{ width: '400px' }} />
    </div>

    <div className="grid-4" style={{ marginBottom: '2rem' }}>
      {[...Array(4)].map((_, i) => (
        <div key={i} className="card">
          <Skeleton className="skeleton-text" style={{ width: '120px', marginBottom: '1rem' }} />
          <Skeleton className="skeleton-text" style={{ width: '60px', height: '2.5rem', marginBottom: '1rem' }} />
          <Skeleton className="skeleton-text" style={{ width: '140px' }} />
        </div>
      ))}
    </div>

    <div className="card">
      <Skeleton className="skeleton-text" style={{ width: '150px', height: '1.25rem', marginBottom: '1.5rem' }} />
      <div style={{ display: 'flex', gap: '1rem' }}>
        <Skeleton className="skeleton-text" style={{ width: '160px', height: '2.5rem' }} />
        <Skeleton className="skeleton-text" style={{ width: '180px', height: '2.5rem' }} />
        <Skeleton className="skeleton-text" style={{ width: '180px', height: '2.5rem' }} />
      </div>
    </div>
  </div>
);

export const RepositoriesSkeleton: React.FC = () => (
  <div>
    <div style={{ marginBottom: '2rem' }}>
      <Skeleton className="skeleton-text" style={{ width: '250px', height: '1.75rem', marginBottom: '0.5rem' }} />
      <Skeleton className="skeleton-text" style={{ width: '500px' }} />
    </div>

    <section style={{ marginBottom: '3rem' }}>
      <Skeleton className="skeleton-text" style={{ width: '200px', height: '1.5rem', marginBottom: '1.5rem' }} />
      <div className="grid-2">
        {[...Array(2)].map((_, i) => (
          <div key={i} className="card">
            <div className="card-header">
              <Skeleton className="skeleton-text" style={{ width: '180px', height: '1.25rem' }} />
              <Skeleton className="skeleton-text" style={{ width: '80px', borderRadius: '9999px' }} />
            </div>
            <Skeleton className="skeleton-text" style={{ width: '100%', marginBottom: '0.5rem' }} />
            <Skeleton className="skeleton-text" style={{ width: '80%', marginBottom: '1.5rem' }} />
            <div style={{ display: 'flex', gap: '0.75rem' }}>
              <Skeleton className="skeleton-text" style={{ width: '100px', height: '2rem' }} />
              <Skeleton className="skeleton-text" style={{ width: '100px', height: '2rem' }} />
            </div>
          </div>
        ))}
      </div>
    </section>
  </div>
);

export const RulesSkeleton: React.FC = () => (
  <div>
    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '2rem' }}>
      <div>
        <Skeleton className="skeleton-text" style={{ width: '200px', height: '1.75rem', marginBottom: '0.5rem' }} />
        <Skeleton className="skeleton-text" style={{ width: '450px' }} />
      </div>
      <Skeleton className="skeleton-text" style={{ width: '140px', height: '2.5rem' }} />
    </div>

    <div className="card" style={{ padding: '1rem 1.25rem', marginBottom: '1.5rem' }}>
      <Skeleton className="skeleton-text" style={{ width: '300px', height: '2rem' }} />
    </div>

    <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      {[...Array(3)].map((_, i) => (
        <div key={i} className="card">
          <div className="card-header" style={{ marginBottom: '0.5rem' }}>
            <Skeleton className="skeleton-text" style={{ width: '150px', height: '1.25rem' }} />
            <Skeleton className="skeleton-text" style={{ width: '60px', height: '22px', borderRadius: '22px' }} />
          </div>
          <Skeleton className="skeleton-text" style={{ width: '100%', height: '40px', marginBottom: '1rem' }} />
          <div style={{ display: 'flex', gap: '0.5rem' }}>
            <Skeleton className="skeleton-text" style={{ width: '80px', height: '2rem' }} />
            <Skeleton className="skeleton-text" style={{ width: '80px', height: '2rem' }} />
          </div>
        </div>
      ))}
    </div>
  </div>
);

export const ActivitySkeleton: React.FC = () => (
  <div>
    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '2rem' }}>
      <div>
        <Skeleton className="skeleton-text" style={{ width: '220px', height: '1.75rem', marginBottom: '0.5rem' }} />
        <Skeleton className="skeleton-text" style={{ width: '400px' }} />
      </div>
      <Skeleton className="skeleton-text" style={{ width: '150px' }} />
    </div>

    <div className="card" style={{ padding: '1rem 1.25rem', marginBottom: '1.5rem', display: 'flex', gap: '1.25rem' }}>
      <Skeleton className="skeleton-text" style={{ width: '220px', height: '2.5rem' }} />
      <Skeleton className="skeleton-text" style={{ width: '150px', height: '2.5rem' }} />
      <Skeleton className="skeleton-text" style={{ width: '150px', height: '2.5rem' }} />
    </div>

    <div className="table-container">
      <table className="data-table">
        <thead>
          <tr>
            <th><Skeleton className="skeleton-text" style={{ width: '100px' }} /></th>
            <th><Skeleton className="skeleton-text" style={{ width: '80px' }} /></th>
            <th><Skeleton className="skeleton-text" style={{ width: '120px' }} /></th>
            <th><Skeleton className="skeleton-text" style={{ width: '150px' }} /></th>
          </tr>
        </thead>
        <tbody>
          {[...Array(5)].map((_, i) => (
            <tr key={i}>
              <td><Skeleton className="skeleton-text" style={{ width: '120px' }} /></td>
              <td><Skeleton className="skeleton-text" style={{ width: '90px' }} /></td>
              <td><Skeleton className="skeleton-text" style={{ width: '180px' }} /></td>
              <td><Skeleton className="skeleton-text" style={{ width: '100px' }} /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  </div>
);

export const PageErrorState: React.FC<{ onRetry?: () => void }> = ({ onRetry }) => (
  <div className="page-error">
    <div className="page-error__icon">⚠️</div>
    <div className="page-error__title">Unable to load this page</div>
    <div className="page-error__body">There was a problem fetching the data.</div>
    {onRetry && (
      <button onClick={onRetry} className="btn btn-outline">
        Retry
      </button>
    )}
  </div>
);
