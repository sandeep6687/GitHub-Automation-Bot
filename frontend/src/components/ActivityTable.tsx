import React, { useState } from 'react';
import type { ActivityEvent } from '../types/activity';
import { StatusBadge } from './StatusBadge';

interface ActivityTableProps {
  events: ActivityEvent[];
  onRefresh: () => void;
  isRefreshing?: boolean;
}

export const ActivityTable: React.FC<ActivityTableProps> = ({
  events,
  onRefresh,
  isRefreshing = false,
}) => {
  const [expandedRow, setExpandedRow] = useState<string | null>(null);

  const toggleRow = (eventId: string) => {
    setExpandedRow(expandedRow === eventId ? null : eventId);
  };

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
        <p style={{ fontSize: '0.875rem' }}>
          Showing <strong>{events.length}</strong> recent events. Click any row to view individual action executions.
        </p>
        <button
          onClick={onRefresh}
          disabled={isRefreshing}
          className="btn btn-outline btn-sm"
        >
          {isRefreshing ? 'Refreshing...' : '↻ Refresh'}
        </button>
      </div>

      <div className="table-container">
        <table className="data-table">
          <thead>
            <tr>
              <th style={{ width: '180px' }}>Received At</th>
              <th>Event</th>
              <th>Action</th>
              <th style={{ width: '130px' }}>Status</th>
              <th style={{ width: '90px', textAlign: 'center' }}>Attempts</th>
              <th style={{ width: '120px' }}>Actions Run</th>
            </tr>
          </thead>
          <tbody>
            {events.length === 0 ? (
              <tr>
                <td colSpan={6} style={{ textAlign: 'center', padding: '3rem', color: 'var(--text-muted)' }}>
                  No webhook events recorded yet for this repository.
                </td>
              </tr>
            ) : (
              events.map((evt) => {
                const isExpanded = expandedRow === evt.eventId;
                return (
                  <React.Fragment key={evt.eventId}>
                    <tr
                      className="clickable"
                      onClick={() => toggleRow(evt.eventId)}
                      style={{
                        backgroundColor: isExpanded ? 'rgba(99, 102, 241, 0.08)' : undefined,
                      }}
                    >
                      <td style={{ fontSize: '0.8125rem', color: 'var(--text-secondary)' }}>
                        {new Date(evt.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })} · {new Date(evt.createdAt).toLocaleDateString()}
                      </td>
                      <td>
                        <span className="code-tag">{evt.eventType}</span>
                      </td>
                      <td>
                        {evt.action ? (
                          <span style={{ fontSize: '0.8125rem', color: 'var(--text-secondary)' }}>
                            {evt.action}
                          </span>
                        ) : (
                          <span style={{ color: 'var(--text-muted)' }}>—</span>
                        )}
                      </td>
                      <td>
                        <StatusBadge status={evt.status} />
                      </td>
                      <td style={{ textAlign: 'center', fontSize: '0.875rem', fontWeight: 600 }}>
                        {evt.attemptCount}
                      </td>
                      <td>
                        <span style={{ fontSize: '0.8125rem', color: 'var(--text-secondary)' }}>
                          {evt.actions.length} action{evt.actions.length === 1 ? '' : 's'} {isExpanded ? '▲' : '▼'}
                        </span>
                      </td>
                    </tr>

                    {/* Expandable Details Row */}
                    {isExpanded && (
                      <tr>
                        <td colSpan={6} style={{ backgroundColor: 'var(--bg-primary)', padding: '1rem 1.5rem', borderBottom: '1px solid var(--border-color)' }}>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                            <div style={{ display: 'flex', gap: '2rem', fontSize: '0.8125rem' }}>
                              <div>
                                <span style={{ color: 'var(--text-muted)' }}>Delivery ID: </span>
                                <span className="code-tag">{evt.deliveryId}</span>
                              </div>
                              {evt.processedAt && (
                                <div>
                                  <span style={{ color: 'var(--text-muted)' }}>Processed At: </span>
                                  <span>{new Date(evt.processedAt).toLocaleTimeString()}</span>
                                </div>
                              )}
                            </div>

                            {evt.lastError && (
                              <div style={{ background: 'var(--danger-bg)', border: '1px solid var(--danger-border)', padding: '0.5rem 0.75rem', borderRadius: '0.25rem', color: 'var(--danger-text)', fontSize: '0.8125rem' }}>
                                <strong>Error:</strong> {evt.lastError}
                              </div>
                            )}

                            <div>
                              <h4 style={{ fontSize: '0.8125rem', color: 'var(--text-secondary)', textTransform: 'uppercase', marginBottom: '0.5rem' }}>
                                Action Execution History
                              </h4>
                              {evt.actions.length === 0 ? (
                                <p style={{ fontSize: '0.8125rem', color: 'var(--text-muted)' }}>
                                  No actions configured or matched for this event.
                                </p>
                              ) : (
                                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.375rem' }}>
                                  {evt.actions.map((act, actIdx) => (
                                    <div
                                      key={actIdx}
                                      style={{
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'space-between',
                                        background: 'var(--bg-secondary)',
                                        padding: '0.5rem 0.75rem',
                                        borderRadius: '0.25rem',
                                        border: '1px solid var(--border-color)',
                                        fontSize: '0.8125rem',
                                      }}
                                    >
                                      <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                                        <span style={{ fontWeight: 600 }}>
                                          {act.status.toUpperCase() === 'SUCCESS' ? '✓' : '✕'} {act.actionType}
                                        </span>
                                        {act.durationMs != null && (
                                          <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>
                                            {act.durationMs}ms
                                          </span>
                                        )}
                                      </div>

                                      <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                                        {act.errorMessage && (
                                          <span style={{ color: 'var(--danger-text)', fontSize: '0.75rem' }}>
                                            {act.errorMessage}
                                          </span>
                                        )}
                                        <StatusBadge status={act.status} />
                                      </div>
                                    </div>
                                  ))}
                                </div>
                              )}
                            </div>
                          </div>
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};
