import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import App from '../App';
import { ragClient } from '../api/ragClient';

describe('Medango Style DNA UI Integration', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(ragClient, 'getDocuments').mockResolvedValue([]);
    vi.spyOn(ragClient, 'getAuditMetrics').mockResolvedValue({
      totalQueries: 12,
      totalTokens: 1250,
      totalCostUsd: 0.0012,
      cacheHitRatio: 0.67,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders top navigation with brand glyph, quick search pill, and keyboard shortcut badge', async () => {
    render(<App />);

    // Brand logo and title
    expect(screen.getByText('Enterprise Knowledge RAG Engine')).toBeInTheDocument();
    expect(screen.getByTestId('brand-logo-glyph')).toBeInTheDocument();

    // Quick search pill & shortcut badge
    const searchInput = screen.getByTestId('global-search-input');
    expect(searchInput).toBeInTheDocument();
    expect(searchInput).toHaveAttribute('placeholder', 'Quick search...');
    expect(screen.getByTestId('search-shortcut-badge')).toBeInTheDocument();
    expect(screen.getByTestId('search-shortcut-badge')).toHaveTextContent('⌘ S');
  });

  it('renders segmented pill navigation tabs and allows switching active tab', async () => {
    render(<App />);

    const tabsNav = screen.getByTestId('rag-navigation-tabs');
    expect(tabsNav).toBeInTheDocument();

    // Verify key tabs
    expect(screen.getByRole('tab', { name: /overview/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /documents/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /query studio/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /telemetry/i })).toBeInTheDocument();

    // Switch tab
    const documentsTab = screen.getByRole('tab', { name: /documents/i });
    fireEvent.click(documentsTab);
    expect(documentsTab).toHaveClass('rag-tab-pill--active');
  });

  it('renders auditor profile card with warm sunburst header and badges', async () => {
    render(<App />);

    expect(screen.getByTestId('auditor-profile-card')).toBeInTheDocument();
    expect(screen.getByTestId('profile-sunburst-header')).toBeInTheDocument();
    expect(screen.getByText(/Lead Compliance Auditor/i)).toBeInTheDocument();
    expect(screen.getByTestId('profile-connected-badges')).toBeInTheDocument();
  });

  it('renders dark contrast bento cards for connected engines telemetry with toggles', async () => {
    render(<App />);

    // Connected engines section
    expect(screen.getByTestId('connected-engines-grid')).toBeInTheDocument();
    expect(screen.getByText(/Qdrant Vector DB/i)).toBeInTheDocument();
    expect(screen.getByText(/Redis Semantic Cache/i)).toBeInTheDocument();

    // iOS style toggles
    const toggles = screen.getAllByTestId('engine-toggle-switch');
    expect(toggles.length).toBeGreaterThanOrEqual(2);
  });

  it('preserves all core workflow test IDs and critical elements', async () => {
    render(<App />);

    // Dropzone and file upload
    expect(screen.getByTestId('document-upload-dropzone')).toBeInTheDocument();
    expect(screen.getByTestId('document-file-input')).toBeInTheDocument();
    expect(screen.getByTestId('document-upload-button')).toBeInTheDocument();

    // Document table
    expect(screen.getByTestId('document-table')).toBeInTheDocument();

    // Chat interface
    expect(screen.getByTestId('rag-chat-interface')).toBeInTheDocument();
    expect(screen.getByTestId('query-input')).toBeInTheDocument();
    expect(screen.getByTestId('query-submit-button')).toBeInTheDocument();
  });
});
