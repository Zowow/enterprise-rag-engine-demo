import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { DocumentUploadZone } from '../components/DocumentUploadZone';
import { IngestionProgressBar } from '../components/IngestionProgressBar';
import { DocumentListTable } from '../components/DocumentListTable';
import { ragClient } from '../api/ragClient';
import type { DocumentItem, DocumentUploadResult } from '../types/rag';

describe('Ingestion UI Components', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('DocumentUploadZone', () => {
    it('renders dropzone, file input, and upload button with required test IDs', () => {
      render(<DocumentUploadZone />);

      expect(screen.getByTestId('document-upload-dropzone')).toBeInTheDocument();
      expect(screen.getByTestId('document-file-input')).toBeInTheDocument();
      expect(screen.getByTestId('document-upload-button')).toBeInTheDocument();
      expect(screen.getByTestId('document-upload-button')).toBeDisabled();
    });

    it('rejects file larger than 25MB and displays validation error', async () => {
      render(<DocumentUploadZone />);

      const fileInput = screen.getByTestId('document-file-input') as HTMLInputElement;
      // 26 MB file
      const largeFile = new File(['x'.repeat(100)], 'huge-policy.pdf', {
        type: 'application/pdf',
      });
      Object.defineProperty(largeFile, 'size', { value: 26 * 1024 * 1024 });

      fireEvent.change(fileInput, { target: { files: [largeFile] } });

      expect(
        await screen.findByText(/file size exceeds 25mb limit/i)
      ).toBeInTheDocument();
      expect(screen.getByTestId('document-upload-button')).toBeDisabled();
    });

    it('rejects unsupported file extensions and displays validation error', async () => {
      render(<DocumentUploadZone />);

      const fileInput = screen.getByTestId('document-file-input') as HTMLInputElement;
      const invalidFile = new File(['executable'], 'malicious.exe', {
        type: 'application/octet-stream',
      });

      fireEvent.change(fileInput, { target: { files: [invalidFile] } });

      expect(
        await screen.findByText(/unsupported file format/i)
      ).toBeInTheDocument();
      expect(screen.getByTestId('document-upload-button')).toBeDisabled();
    });

    it('accepts valid PDF file, enables upload button, and displays file info', async () => {
      render(<DocumentUploadZone />);

      const fileInput = screen.getByTestId('document-file-input') as HTMLInputElement;
      const validFile = new File(['valid pdf content'], 'compliance-manual.pdf', {
        type: 'application/pdf',
      });
      Object.defineProperty(validFile, 'size', { value: 2 * 1024 * 1024 }); // 2MB

      fireEvent.change(fileInput, { target: { files: [validFile] } });

      expect(screen.getByText('compliance-manual.pdf')).toBeInTheDocument();
      expect(screen.getByText(/2(\.0)? MB/i)).toBeInTheDocument();
      expect(screen.getByTestId('document-upload-button')).not.toBeDisabled();
    });

    it('successfully uploads file and triggers onUploadSuccess callback', async () => {
      const mockResult: DocumentUploadResult = {
        jobId: 'job-999',
        documentId: 'doc-888',
        title: 'compliance-manual.pdf',
        status: 'Queued',
      };

      const uploadSpy = vi
        .spyOn(ragClient, 'uploadDocument')
        .mockResolvedValueOnce(mockResult);

      const onUploadSuccess = vi.fn();

      render(<DocumentUploadZone onUploadSuccess={onUploadSuccess} />);

      const fileInput = screen.getByTestId('document-file-input') as HTMLInputElement;
      const validFile = new File(['sample markdown'], 'readme.md', {
        type: 'text/markdown',
      });
      Object.defineProperty(validFile, 'size', { value: 1024 }); // 1KB

      fireEvent.change(fileInput, { target: { files: [validFile] } });

      const uploadButton = screen.getByTestId('document-upload-button');
      fireEvent.click(uploadButton);

      await waitFor(() => {
        expect(uploadSpy).toHaveBeenCalledWith(validFile, 'readme.md');
        expect(onUploadSuccess).toHaveBeenCalledWith(mockResult);
      });
    });

    it('handles upload errors gracefully and displays error banner', async () => {
      vi.spyOn(ragClient, 'uploadDocument').mockRejectedValueOnce(
        new Error('Upload service unavailable')
      );

      render(<DocumentUploadZone />);

      const fileInput = screen.getByTestId('document-file-input') as HTMLInputElement;
      const validFile = new File(['doc content'], 'contract.docx', {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      });
      Object.defineProperty(validFile, 'size', { value: 5000 });

      fireEvent.change(fileInput, { target: { files: [validFile] } });

      const uploadButton = screen.getByTestId('document-upload-button');
      fireEvent.click(uploadButton);

      expect(
        await screen.findByText(/upload service unavailable/i)
      ).toBeInTheDocument();
    });
  });

  describe('IngestionProgressBar', () => {
    it('renders progress bar and status badge with correct values', () => {
      render(
        <IngestionProgressBar
          jobId="job-123"
          status="Parsing"
          progressPercentage={45}
          message="Parsing PDF structure and pages..."
        />
      );

      const progressBar = screen.getByTestId('ingestion-progress-bar');
      const statusBadge = screen.getByTestId('ingestion-status-badge');

      expect(progressBar).toBeInTheDocument();
      expect(statusBadge).toBeInTheDocument();
      expect(statusBadge).toHaveTextContent('Parsing');
      expect(screen.getByText('45%')).toBeInTheDocument();
      expect(
        screen.getByText('Parsing PDF structure and pages...')
      ).toBeInTheDocument();
    });

    it('renders completed state with 100% progress', () => {
      render(
        <IngestionProgressBar
          jobId="job-123"
          status="Completed"
          progressPercentage={100}
          message="Document indexed successfully into vector store."
        />
      );

      const statusBadge = screen.getByTestId('ingestion-status-badge');
      expect(statusBadge).toHaveTextContent('Completed');
      expect(screen.getByText('100%')).toBeInTheDocument();
    });

    it('renders failed state with failure details', () => {
      render(
        <IngestionProgressBar
          jobId="job-123"
          status="Failed"
          progressPercentage={25}
          message="No extractable text found"
        />
      );

      const statusBadge = screen.getByTestId('ingestion-status-badge');
      expect(statusBadge).toHaveTextContent('Failed');
      expect(screen.getByText('No extractable text found')).toBeInTheDocument();
    });
  });

  describe('DocumentListTable', () => {
    it('renders empty state when document list is empty', () => {
      render(<DocumentListTable documents={[]} />);

      expect(screen.getByTestId('document-table')).toBeInTheDocument();
      expect(screen.getByTestId('document-empty-state')).toBeInTheDocument();
      expect(screen.getByText(/no documents uploaded yet/i)).toBeInTheDocument();
    });

    it('renders document rows with metadata, formatted sizes, and status badges', () => {
      const mockDocuments: DocumentItem[] = [
        {
          id: 'doc-1',
          title: 'Employee Handbook 2026',
          fileName: 'handbook.pdf',
          fileSizeBytes: 1048576, // 1 MB
          status: 'Completed',
          chunkCount: 14,
          createdAt: '2026-09-10T08:30:00Z',
        },
        {
          id: 'doc-2',
          title: 'Financial Audit Report',
          fileName: 'audit.docx',
          fileSizeBytes: 524288, // 512 KB
          status: 'Processing',
          chunkCount: 0,
          createdAt: '2026-09-10T09:00:00Z',
        },
      ];

      render(<DocumentListTable documents={mockDocuments} />);

      const rows = screen.getAllByTestId('document-row');
      expect(rows).toHaveLength(2);

      expect(screen.getByText('Employee Handbook 2026')).toBeInTheDocument();
      expect(screen.getByText('Financial Audit Report')).toBeInTheDocument();
      expect(screen.getByText(/1(\.0)? MB/i)).toBeInTheDocument();
      expect(screen.getByText(/512 KB/i)).toBeInTheDocument();

      const statusBadges = screen.getAllByTestId('ingestion-status-badge');
      expect(statusBadges).toHaveLength(2);
      expect(statusBadges[0]).toHaveTextContent('Completed');
      expect(statusBadges[1]).toHaveTextContent('Processing');
    });

    it('triggers onRefresh callback when refresh button is clicked', () => {
      const onRefresh = vi.fn();
      render(<DocumentListTable documents={[]} onRefresh={onRefresh} />);

      const refreshButton = screen.getByTestId('document-refresh-button');
      fireEvent.click(refreshButton);

      expect(onRefresh).toHaveBeenCalledTimes(1);
    });
  });
});
