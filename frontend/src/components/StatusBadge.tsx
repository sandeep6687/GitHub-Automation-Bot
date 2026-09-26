import React from 'react';

interface StatusBadgeProps {
  status: string;
}

export const StatusBadge: React.FC<StatusBadgeProps> = ({ status }) => {
  const normalized = status.toUpperCase();

  let bgClass = 'bg-slate-700 text-slate-200 border-slate-600';
  let dotClass = 'bg-slate-400';

  if (normalized === 'SUCCESS') {
    bgClass = 'bg-emerald-950/60 text-emerald-300 border-emerald-700/60';
    dotClass = 'bg-emerald-400';
  } else if (normalized === 'RETRYING') {
    bgClass = 'bg-amber-950/60 text-amber-300 border-amber-700/60';
    dotClass = 'bg-amber-400 animate-pulse';
  } else if (normalized === 'FAILED') {
    bgClass = 'bg-rose-950/60 text-rose-300 border-rose-700/60';
    dotClass = 'bg-rose-400';
  } else if (normalized === 'PROCESSING') {
    bgClass = 'bg-sky-950/60 text-sky-300 border-sky-700/60';
    dotClass = 'bg-sky-400 animate-ping';
  } else if (normalized === 'PENDING') {
    bgClass = 'bg-indigo-950/60 text-indigo-300 border-indigo-700/60';
    dotClass = 'bg-indigo-400';
  }

  return (
    <span
      className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium border ${bgClass}`}
    >
      <span className={`w-1.5 h-1.5 rounded-full ${dotClass}`}></span>
      {normalized}
    </span>
  );
};
