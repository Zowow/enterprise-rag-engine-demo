import React, { useState, useEffect, useCallback } from 'react';
import {
  ArrowLeftRight,
  Layers,
  Sparkles,
} from 'lucide-react';
import { DocumentUploadZone } from './components/DocumentUploadZone';
import { IngestionProgressBar } from './components/IngestionProgressBar';
import { DocumentListTable } from './components/DocumentListTable';
import { RagChatInterface } from './components/RagChatInterface';
import { ragClient } from './api/ragClient';
import { useIngestionSignalR } from './hooks/useIngestionSignalR';
import type { DocumentItem, DocumentUploadResult } from './types/rag';

export const App: React.FC = () => {
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [isLoadingDocs, setIsLoadingDocs] = useState<boolean>(false);
  const [activeJob, setActiveJob] = useState<{
    jobId: string;
    documentTitle: string;
  } | null>(null);

  const fetchDocuments = useCallback(async () => {
    setIsLoadingDocs(true);
    try {
      const data = await ragClient.getDocuments();
      setDocuments(data);
    } catch (err) {
      console.error('Failed to load documents:', err);
    } finally {
      setIsLoadingDocs(false);
    }
  }, []);

  const { latestUpdate, joinJob, connectionState } = useIngestionSignalR({
    onProgress: (update) => {
      if (update.status === 'Completed' || update.status === 'Failed') {
        fetchDocuments();
      }
    },
  });

  useEffect(() => {
    fetchDocuments();
  }, [fetchDocuments]);

  const handleUploadSuccess = async (result: DocumentUploadResult) => {
    setActiveJob({
      jobId: result.jobId,
      documentTitle: result.title,
    });
    await joinJob(result.jobId);
    fetchDocuments();
  };

  return (
    <div className="rag-app-layout">
      {/* Medango Top Navigation Bar */}
      <header className="rag-header">
        <div className="rag-header-brand">
          <div data-testid="brand-logo-glyph" className="rag-brand-glyph" title="Enterprise Knowledge Engine">
            <ArrowLeftRight size={20} />
          </div>
          <div className="rag-header-titles">
            <h1 className="rag-brand-title">Enterprise Knowledge RAG Engine</h1>
            <span className="rag-brand-subtitle">
              Verified Compliance &amp; Legal Intelligence Platform
            </span>
          </div>
        </div>

        {/* Right Status Indicator */}
        <div className="rag-header-status">
          <div className="rag-status-indicator">
            <span
              className={`rag-status-dot ${
                connectionState === 'Connected' ? 'rag-status-dot--connected' : ''
              }`}
            />
            <span className="rag-status-text">SignalR: {connectionState}</span>
          </div>
        </div>
      </header>

      {/* Main Dashboard Area */}
      <main className="rag-main-content">
        {/* Main Two-Column Dual-Tone Dashboard */}
        <div className="rag-dashboard-grid">
          {/* Left Column: Knowledge Ingestion & Repository */}
          <section className="rag-dashboard-column">
            <div className="rag-column-header">
              <Layers size={18} className="rag-column-icon" />
              <h2>Knowledge Repository</h2>
            </div>

            {/* Document Upload Zone */}
            <DocumentUploadZone onUploadSuccess={handleUploadSuccess} />

            {/* Live Progress Bar */}
            {(activeJob || latestUpdate) && (
              <IngestionProgressBar
                jobId={latestUpdate?.jobId || activeJob?.jobId}
                documentTitle={activeJob?.documentTitle}
                status={latestUpdate?.status || 'Queued'}
                progressPercentage={latestUpdate?.progressPercentage ?? 5}
                message={
                  latestUpdate?.message || 'Queued in Azurite background worker queue...'
                }
              />
            )}

            {/* Document Index Table */}
            <DocumentListTable
              documents={documents}
              isLoading={isLoadingDocs}
              onRefresh={fetchDocuments}
            />
          </section>

          {/* Right Column: RAG Q&A Interface */}
          <section className="rag-dashboard-column">
            <div className="rag-column-header">
              <Sparkles size={18} className="rag-column-icon" />
              <h2>Verified Q&amp;A Intelligence</h2>
            </div>

            <RagChatInterface />
          </section>
        </div>
      </main>
    </div>
  );
};

export default App;
