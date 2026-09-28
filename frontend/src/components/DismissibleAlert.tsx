import React, { useState } from 'react';

interface DismissibleAlertProps {
  message: React.ReactNode;
  type?: 'success' | 'error' | 'warning' | 'info';
  onDismiss?: () => void;
}

export const DismissibleAlert: React.FC<DismissibleAlertProps> = ({ message, type = 'error', onDismiss }) => {
  const [isVisible, setIsVisible] = useState(true);

  if (!isVisible) return null;

  const bgColors = {
    success: 'var(--success-bg)',
    error: 'var(--danger-bg)',
    warning: '#fff3cd',
    info: '#cce5ff',
  };

  const textColors = {
    success: 'var(--success-text)',
    error: 'var(--danger-text)',
    warning: '#856404',
    info: '#004085',
  };

  const borderColors = {
    success: 'var(--success-border)',
    error: 'var(--danger-border)',
    warning: '#ffeeba',
    info: '#b8daff',
  };

  const handleDismiss = () => {
    setIsVisible(false);
    if (onDismiss) {
      onDismiss();
    }
  };

  return (
    <div
      style={{
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        padding: '0.875rem 1rem',
        borderRadius: '0.375rem',
        marginBottom: '1.5rem',
        fontSize: '0.875rem',
        backgroundColor: bgColors[type],
        color: textColors[type],
        border: `1px solid ${borderColors[type]}`,
      }}
      role="alert"
    >
      <span>{message}</span>
      <button
        onClick={handleDismiss}
        aria-label="Dismiss error"
        style={{
          background: 'none',
          border: 'none',
          color: textColors[type],
          fontSize: '1.25rem',
          lineHeight: 1,
          cursor: 'pointer',
          padding: '0',
          marginLeft: '1rem',
          opacity: 0.7,
        }}
        onMouseOver={(e) => (e.currentTarget.style.opacity = '1')}
        onMouseOut={(e) => (e.currentTarget.style.opacity = '0.7')}
      >
        &times;
      </button>
    </div>
  );
};
