import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import App from '../App';
import { DocumentListTable } from '../components/DocumentListTable';
import { ragClient } from '../api/ragClient';
import type { DocumentItem } from '../types/rag';

const mockDocuments: DocumentItem[] = [
  {
    id: 'doc-1',
    title: 'Security Compliance Handbook',
    fileName: 'security-compliance-handbook.pdf',
    fileSizeBytes: 1048576,
    status: 'Completed',
    failureReason: null,
    chunkCount: 18,
    createdAt: '2026-09-10T12:00:00Z',
    updatedAt: '2026-09-10T12:05:00Z',
  },
  {
    id: 'doc-2',
    title: 'Data Privacy Guidelines',
    fileName: 'privacy-policy.docx',
    fileSizeBytes: 524288,
    status: 'Processing',
    failureReason: null,
    chunkCount: 8,
    createdAt: '2026-09-10T13:00:00Z',
    updatedAt: '2026-09-10T13:01:00Z',
  },
];

describe('Streamlined Medango UI & Feature Pruning (Milestone 14)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(ragClient, 'getDocuments').mockResolvedValue(mockDocuments);
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

  it('renders streamlined top navigation with brand glyph, title, subtitle, and live SignalR status only', async () => {
    render(<App />);

    // Brand logo, title, and subtitle
    expect(screen.getByText('Enterprise Knowledge RAG Engine')).toBeInTheDocument();
    expect(screen.getByText(/Verified Compliance & Legal Intelligence Platform/i)).toBeInTheDocument();
    expect(screen.getByTestId('brand-logo-glyph')).toBeInTheDocument();

    // Live SignalR status text
    expect(screen.getByText(/SignalR:/i)).toBeInTheDocument();

    // Dead header search elements must NOT be present in top nav
    expect(screen.queryByTestId('global-search-input')).toBeNull();
    expect(screen.queryByTestId('search-shortcut-badge')).toBeNull();
  });

  it('prunes dead mock features (profile card, bento switches, and navigation tabs)', async () => {
    render(<App />);

    // Auditor profile card must be removed
    expect(screen.queryByTestId('auditor-profile-card')).toBeNull();
    expect(screen.queryByTestId('profile-sunburst-header')).toBeNull();
    expect(screen.queryByText(/Sarah Jenkins/i)).toBeNull();
    expect(screen.queryByText(/Lead Compliance Auditor/i)).toBeNull();

    // Connected engines bento toggles must be removed
    expect(screen.queryByTestId('connected-engines-grid')).toBeNull();
    expect(screen.queryByTestId('engine-toggle-switch')).toBeNull();

    // Navigation tabs must be removed
    expect(screen.queryByTestId('rag-navigation-tabs')).toBeNull();
    expect(screen.queryByRole('tab', { name: /overview/i })).toBeNull();
    expect(screen.queryByRole('tab', { name: /query studio/i })).toBeNull();
  });

  it('renders streamlined two-column cockpit layout with Knowledge Repository and Q&A Intelligence', async () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: /Knowledge Repository/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Verified Q&A Intelligence/i })).toBeInTheDocument();
  });

  it('renders inline document search in DocumentListTable and filters documents in real-time', async () => {
    render(<DocumentListTable documents={mockDocuments} />);

    // Search input should be present inside table header
    const searchInput = screen.getByTestId('document-search-input');
    expect(searchInput).toBeInTheDocument();
    expect(searchInput).toHaveAttribute('placeholder', 'Filter documents...');

    // Initially all documents are visible
    expect(screen.getByText('Security Compliance Handbook')).toBeInTheDocument();
    expect(screen.getByText('Data Privacy Guidelines')).toBeInTheDocument();

    // Type filter query
    fireEvent.change(searchInput, { target: { value: 'Privacy' } });

    // Filtered state
    expect(screen.queryByText('Security Compliance Handbook')).toBeNull();
    expect(screen.getByText('Data Privacy Guidelines')).toBeInTheDocument();

    // Clear filter query
    fireEvent.change(searchInput, { target: { value: '' } });
    expect(screen.getByText('Security Compliance Handbook')).toBeInTheDocument();
    expect(screen.getByText('Data Privacy Guidelines')).toBeInTheDocument();
  });

  it('displays empty search result state when filter query yields no matches', async () => {
    render(<DocumentListTable documents={mockDocuments} />);

    const searchInput = screen.getByTestId('document-search-input');
    fireEvent.change(searchInput, { target: { value: 'NonexistentKeyword12345' } });

    expect(screen.getByTestId('document-search-empty-state')).toBeInTheDocument();
    expect(screen.getByText(/No documents match your search/i)).toBeInTheDocument();
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
