import React from 'react';
import {
  FileText,
  RefreshCw,
  Layers,
  Calendar,
  HardDrive,
  CheckCircle2,
  XCircle,
  Clock,
  Loader2,
  Inbox,
} from 'lucide-react';
import type { DocumentItem, IngestionStatus } from '../types/rag';

export interface DocumentListTableProps {
  documents: DocumentItem[];
  isLoading?: boolean;
  onRefresh?: () => void;
  className?: string;
}

export const DocumentListTable: React.FC<DocumentListTableProps> = ({
  documents,
  isLoading = false,
  onRefresh,
  className = '',
}) => {
  const formatFileSize = (bytes: number): string => {
    if (!bytes || isNaN(bytes)) return '0 B';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const formatDate = (dateStr: string): string => {
    try {
      const d = new Date(dateStr);
      if (isNaN(d.getTime())) return dateStr;
      return d.toLocaleDateString(undefined, {
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });
    } catch {
      return dateStr;
    }
  };

  const renderStatusBadge = (status: IngestionStatus | string, failureReason?: string | null) => {
    switch (status) {
      case 'Completed':
        return (
          <span
            data-testid="ingestion-status-badge"
            className="rag-badge rag-badge--success"
            title="Document successfully processed and indexed"
          >
            <CheckCircle2 size={13} className="rag-status-icon" />
            <span>Completed</span>
          </span>
        );
      case 'Failed':
        return (
          <span
            data-testid="ingestion-status-badge"
            className="rag-badge rag-badge--danger"
            title={failureReason || 'Ingestion failed'}
          >
            <XCircle size={13} className="rag-status-icon" />
            <span>Failed</span>
          </span>
        );
      case 'Queued':
        return (
          <span
            data-testid="ingestion-status-badge"
            className="rag-badge rag-badge--warning"
            title="Waiting in Azurite ingestion queue"
          >
            <Clock size={13} className="rag-status-icon" />
            <span>Queued</span>
          </span>
        );
      case 'Processing':
      case 'Parsing':
      case 'Embedding':
      default:
        return (
          <span
            data-testid="ingestion-status-badge"
            className="rag-badge rag-badge--primary"
            title="Active processing in progress"
          >
            <Loader2 size={13} className="rag-status-icon rag-spinner" />
            <span>{status}</span>
          </span>
        );
    }
  };

  return (
    <div className={`rag-table-container ${className}`} data-testid="document-table">
      <div className="rag-table-header">
        <div className="rag-table-title-group">
          <h3 className="rag-table-heading">Indexed Documents</h3>
          <span className="rag-table-count">
            {documents.length} {documents.length === 1 ? 'file' : 'files'}
          </span>
        </div>

        {onRefresh && (
          <button
            type="button"
            data-testid="document-refresh-button"
            className="rag-btn rag-btn--secondary rag-btn--sm"
            onClick={onRefresh}
            disabled={isLoading}
            title="Refresh document list"
          >
            <RefreshCw size={14} className={isLoading ? 'rag-spinner' : ''} />
            <span>Refresh</span>
          </button>
        )}
      </div>

      {documents.length === 0 ? (
        <div data-testid="document-empty-state" className="rag-empty-state">
          <div className="rag-empty-icon">
            <Inbox size={42} />
          </div>
          <p className="rag-empty-title">No documents uploaded yet</p>
          <p className="rag-empty-desc">
            Upload legal agreements, compliance manuals, or policy files to begin indexing.
          </p>
        </div>
      ) : (
        <div className="rag-table-scroll">
          <table className="rag-table">
            <thead>
              <tr>
                <th>Document</th>
                <th>Status</th>
                <th>Size</th>
                <th>Chunks</th>
                <th>Uploaded</th>
              </tr>
            </thead>
            <tbody>
              {documents.map((doc) => (
                <tr key={doc.id} data-testid="document-row" className="rag-table-row">
                  <td className="rag-td-title">
                    <div className="rag-doc-cell">
                      <FileText size={18} className="rag-doc-icon" />
                      <div className="rag-doc-info">
                        <span className="rag-doc-title" title={doc.title}>
                          {doc.title}
                        </span>
                        {doc.fileName && doc.fileName !== doc.title && (
                          <span className="rag-doc-filename" title={doc.fileName}>
                            {doc.fileName}
                          </span>
                        )}
                        {doc.status === 'Failed' && doc.failureReason && (
                          <span className="rag-doc-failure-reason" title={doc.failureReason}>
                            Reason: {doc.failureReason}
                          </span>
                        )}
                      </div>
                    </div>
                  </td>
                  <td className="rag-td-status">
                    {renderStatusBadge(doc.status, doc.failureReason)}
                  </td>
                  <td className="rag-td-size">
                    <span className="rag-stat-cell">
                      <HardDrive size={13} className="rag-stat-icon" />
                      {formatFileSize(doc.fileSizeBytes)}
                    </span>
                  </td>
                  <td className="rag-td-chunks">
                    <span className="rag-stat-cell">
                      <Layers size={13} className="rag-stat-icon" />
                      {doc.chunkCount ?? 0}
                    </span>
                  </td>
                  <td className="rag-td-date">
                    <span className="rag-stat-cell">
                      <Calendar size={13} className="rag-stat-icon" />
                      {formatDate(doc.createdAt)}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

export default DocumentListTable;
