import React, { useState, useEffect, useCallback } from 'react';
import {
  ArrowLeftRight,
  Search,
  Database,
  Zap,
  Layers,
  Activity,
  FileText,
  Sparkles,
  Cloud,
  ShieldCheck,
  User,
} from 'lucide-react';
import { DocumentUploadZone } from './components/DocumentUploadZone';
import { IngestionProgressBar } from './components/IngestionProgressBar';
import { DocumentListTable } from './components/DocumentListTable';
import { RagChatInterface } from './components/RagChatInterface';
import { ragClient } from './api/ragClient';
import { useIngestionSignalR } from './hooks/useIngestionSignalR';
import type { DocumentItem, DocumentUploadResult } from './types/rag';

type NavigationTab = 'overview' | 'documents' | 'query' | 'telemetry';

export const App: React.FC = () => {
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [isLoadingDocs, setIsLoadingDocs] = useState<boolean>(false);
  const [activeJob, setActiveJob] = useState<{
    jobId: string;
    documentTitle: string;
  } | null>(null);

  // Style DNA: Top Nav Search and Segmented Tabs
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [activeTab, setActiveTab] = useState<NavigationTab>('overview');

  // Style DNA: Connected Engine Toggles
  const [isQdrantActive, setIsQdrantActive] = useState<boolean>(true);
  const [isRedisCacheActive, setIsRedisCacheActive] = useState<boolean>(true);

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

  const filteredDocuments = documents.filter((doc) => {
    if (!searchQuery.trim()) return true;
    const q = searchQuery.toLowerCase();
    return (
      doc.title.toLowerCase().includes(q) ||
      doc.fileName.toLowerCase().includes(q)
    );
  });

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

        {/* Center Quick Search Pill */}
        <div className="rag-header-center">
          <div className="rag-search-pill">
            <Search size={16} color="var(--text-tertiary)" />
            <input
              type="text"
              data-testid="global-search-input"
              className="rag-search-input"
              placeholder="Quick search..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
            <span data-testid="search-shortcut-badge" className="rag-search-badge">
              ⌘ S
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
        {/* Segmented Pill Navigation Bar */}
        <nav
          data-testid="rag-navigation-tabs"
          className="rag-tabs-pill-nav"
          role="tablist"
          aria-label="Dashboard Navigation"
        >
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'overview'}
            className={`rag-tab-pill ${activeTab === 'overview' ? 'rag-tab-pill--active' : ''}`}
            onClick={() => setActiveTab('overview')}
          >
            <Activity size={14} />
            <span>Overview</span>
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'documents'}
            className={`rag-tab-pill ${activeTab === 'documents' ? 'rag-tab-pill--active' : ''}`}
            onClick={() => setActiveTab('documents')}
          >
            <FileText size={14} />
            <span>Documents</span>
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'query'}
            className={`rag-tab-pill ${activeTab === 'query' ? 'rag-tab-pill--active' : ''}`}
            onClick={() => setActiveTab('query')}
          >
            <Sparkles size={14} />
            <span>Query Studio</span>
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'telemetry'}
            className={`rag-tab-pill ${activeTab === 'telemetry' ? 'rag-tab-pill--active' : ''}`}
            onClick={() => setActiveTab('telemetry')}
          >
            <Zap size={14} />
            <span>Telemetry</span>
          </button>
        </nav>

        {/* Main Two-Column Dual-Tone Dashboard */}
        <div className="rag-dashboard-grid">
          {/* Left Column: Profile, Connected Engines & Ingestion Management */}
          <section className="rag-dashboard-column">
            {/* Auditor Sunburst Profile Card */}
            <div data-testid="auditor-profile-card" className="rag-profile-card">
              <div data-testid="profile-sunburst-header" className="rag-sunburst-header">
                <div className="rag-profile-avatar-wrap">
                  <User size={30} />
                </div>
              </div>
              <div className="rag-profile-body">
                <span className="rag-profile-name">Sarah Jenkins</span>
                <span className="rag-profile-role">Lead Compliance Auditor</span>
                <div data-testid="profile-connected-badges" className="rag-profile-badges">
                  <div className="rag-profile-badge-tile" title="Slack Auditing">
                    <ShieldCheck size={16} />
                  </div>
                  <div className="rag-profile-badge-tile" title="Azurite Cloud Storage">
                    <Cloud size={16} />
                  </div>
                  <div className="rag-profile-badge-tile" title="Qdrant Vector Cluster">
                    <Database size={16} />
                  </div>
                </div>
              </div>
            </div>

            {/* Connected Engines Bento Cards (Dark Contrast) */}
            <div data-testid="connected-engines-grid" className="rag-bento-grid">
              <div className="rag-bento-card--dark">
                <div className="rag-bento-header">
                  <span className="rag-bento-title">
                    <Database size={16} color="#60A5FA" />
                    Qdrant Vector DB
                  </span>
                  <button
                    type="button"
                    data-testid="engine-toggle-switch"
                    className={`rag-toggle-switch ${isQdrantActive ? 'rag-toggle-switch--active' : ''}`}
                    onClick={() => setIsQdrantActive(!isQdrantActive)}
                    aria-label="Toggle Qdrant Vector DB sync"
                  >
                    <span className="rag-toggle-thumb" />
                  </button>
                </div>
                <p className="rag-bento-desc">
                  1536-dim embeddings synchronized with collection documents.
                </p>
              </div>

              <div className="rag-bento-card--dark">
                <div className="rag-bento-header">
                  <span className="rag-bento-title">
                    <Zap size={16} color="#C084FC" />
                    Redis Semantic Cache
                  </span>
                  <button
                    type="button"
                    data-testid="engine-toggle-switch"
                    className={`rag-toggle-switch ${isRedisCacheActive ? 'rag-toggle-switch--active' : ''}`}
                    onClick={() => setIsRedisCacheActive(!isRedisCacheActive)}
                    aria-label="Toggle Redis Semantic Cache"
                  >
                    <span className="rag-toggle-thumb" />
                  </button>
                </div>
                <p className="rag-bento-desc">
                  Sub-30ms retrieval active (cosine similarity threshold &ge; 0.95).
                </p>
              </div>
            </div>

            {/* Ingestion & Repository Section */}
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
              documents={filteredDocuments}
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
