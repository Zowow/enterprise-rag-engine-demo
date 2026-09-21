import React from 'react';
import { Loader2, CheckCircle2, XCircle, Clock } from 'lucide-react';
import type { IngestionStatus } from '../types/rag';

export interface IngestionProgressBarProps {
  jobId?: string;
  status: IngestionStatus | string;
  progressPercentage: number;
  message?: string;
  documentTitle?: string;
  className?: string;
}

export const IngestionProgressBar: React.FC<IngestionProgressBarProps> = ({
  jobId,
  status,
  progressPercentage,
  message,
  documentTitle,
  className = '',
}) => {
  const clampedProgress = Math.min(100, Math.max(0, Math.round(progressPercentage)));

  const getStatusDetails = () => {
    switch (status) {
      case 'Completed':
        return {
          icon: <CheckCircle2 size={16} className="rag-status-icon rag-status-icon--success" />,
          variantClass: 'rag-badge--success',
          barClass: 'rag-progress-fill--success',
        };
      case 'Failed':
        return {
          icon: <XCircle size={16} className="rag-status-icon rag-status-icon--danger" />,
          variantClass: 'rag-badge--danger',
          barClass: 'rag-progress-fill--danger',
        };
      case 'Queued':
        return {
          icon: <Clock size={16} className="rag-status-icon rag-status-icon--warning" />,
          variantClass: 'rag-badge--warning',
          barClass: 'rag-progress-fill--warning',
        };
      case 'Processing':
      case 'Parsing':
      case 'Embedding':
      default:
        return {
          icon: <Loader2 size={16} className="rag-status-icon rag-spinner" />,
          variantClass: 'rag-badge--primary',
          barClass: 'rag-progress-fill--active',
        };
    }
  };

  const { icon, variantClass, barClass } = getStatusDetails();

  return (
    <div
      data-testid="ingestion-progress-bar"
      className={`rag-progress-card ${className}`}
      role="progressbar"
      aria-valuenow={clampedProgress}
      aria-valuemin={0}
      aria-valuemax={100}
    >
      <div className="rag-progress-header">
        <div className="rag-progress-meta">
          {documentTitle && (
            <span className="rag-progress-title" title={documentTitle}>
              {documentTitle}
            </span>
          )}
          {jobId && (
            <span className="rag-progress-job-id" title={`Job ID: ${jobId}`}>
              #{jobId.slice(0, 8)}
            </span>
          )}
        </div>

        <div className="rag-progress-status-wrap">
          <span
            data-testid="ingestion-status-badge"
            className={`rag-badge ${variantClass}`}
          >
            {icon}
            <span>{status}</span>
          </span>
          <span className="rag-progress-percentage">{clampedProgress}%</span>
        </div>
      </div>

      <div className="rag-progress-track">
        <div
          className={`rag-progress-fill ${barClass}`}
          style={{ width: `${clampedProgress}%` }}
        />
      </div>

      {message && (
        <div className="rag-progress-footer">
          <span className="rag-progress-message">{message}</span>
        </div>
      )}
    </div>
  );
};

export default IngestionProgressBar;
