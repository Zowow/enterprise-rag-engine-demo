import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { RagClient, RateLimitError } from '../api/ragClient';
import type {
  DocumentUploadResult,
  DocumentItem,
  RagQueryResult,
  AuditMetrics,
} from '../types/rag';

describe('RagClient', () => {
  let client: RagClient;

  beforeEach(() => {
    client = new RagClient('http://localhost:5000');
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('uploadDocument', () => {
    it('uploads a file with multipart/form-data and returns DocumentUploadResult', async () => {
      const mockResult: DocumentUploadResult = {
        jobId: 'job-123',
        documentId: 'doc-456',
        title: 'policy.pdf',
        status: 'Queued',
      };

      const postSpy = vi.spyOn(client.axiosInstance, 'post').mockResolvedValueOnce({
        status: 202,
        data: {
          success: true,
          data: mockResult,
        },
      });

      const file = new File(['fake content'], 'policy.pdf', { type: 'application/pdf' });
      const result = await client.uploadDocument(file, 'Custom Title');

      expect(postSpy).toHaveBeenCalledWith(
        '/api/v1/documents/upload',
        expect.any(FormData),
        expect.objectContaining({
          headers: expect.objectContaining({
            'Content-Type': 'multipart/form-data',
          }),
        })
      );
      expect(result).toEqual(mockResult);
    });

    it('throws error when upload fails with 400', async () => {
      vi.spyOn(client.axiosInstance, 'post').mockRejectedValueOnce({
        response: {
          status: 400,
          data: { success: false, error: 'File exceeds 25MB limit.' },
        },
      });

      const file = new File(['too large'], 'large.pdf', { type: 'application/pdf' });
      await expect(client.uploadDocument(file)).rejects.toThrow('File exceeds 25MB limit.');
    });
  });

  describe('getDocuments', () => {
    it('fetches all documents list', async () => {
      const mockDocs: DocumentItem[] = [
        {
          id: 'doc-1',
          title: 'Doc 1',
          fileName: 'doc1.pdf',
          fileSizeBytes: 1024,
          status: 'Completed',
          chunkCount: 5,
          createdAt: '2026-09-10T00:00:00Z',
        },
      ];

      vi.spyOn(client.axiosInstance, 'get').mockResolvedValueOnce({
        status: 200,
        data: { success: true, data: mockDocs },
      });

      const docs = await client.getDocuments();
      expect(docs).toEqual(mockDocs);
    });
  });

  describe('getDocumentById', () => {
    it('fetches single document status by ID', async () => {
      const mockDoc: DocumentItem = {
        id: 'doc-123',
        title: 'Doc 123',
        fileName: 'doc123.pdf',
        fileSizeBytes: 2048,
        status: 'Processing',
        chunkCount: 2,
        createdAt: '2026-09-10T00:00:00Z',
      };

      vi.spyOn(client.axiosInstance, 'get').mockResolvedValueOnce({
        status: 200,
        data: { success: true, data: mockDoc },
      });

      const doc = await client.getDocumentById('doc-123');
      expect(doc).toEqual(mockDoc);
    });
  });

  describe('queryRag', () => {
    it('queries RAG engine and extracts X-Cache header info', async () => {
      const mockResult: RagQueryResult = {
        answer: 'Compliance requires annual audits.',
        citations: [
          {
            documentTitle: 'audit-policy.pdf',
            pageNumber: 3,
            excerpt: 'Section 4: annual audits required.',
          },
        ],
        isCached: true,
        costUsd: 0,
      };

      vi.spyOn(client.axiosInstance, 'post').mockResolvedValueOnce({
        status: 200,
        headers: {
          'x-cache': 'HIT-SEMANTIC',
          'x-ratelimit-remaining': '19',
        },
        data: {
          success: true,
          data: mockResult,
        },
      });

      const response = await client.queryRag('What are the audit requirements?');
      expect(response.result).toEqual(mockResult);
      expect(response.isSemanticCacheHit).toBe(true);
      expect(response.rateLimitRemaining).toBe(19);
    });

    it('parses HTTP 429 rate limit with Retry-After header into RateLimitError', async () => {
      vi.spyOn(client.axiosInstance, 'post').mockRejectedValueOnce({
        isAxiosError: true,
        response: {
          status: 429,
          headers: {
            'retry-after': '45',
          },
          data: {
            success: false,
            error: 'Rate limit exceeded. Please wait before submitting more queries.',
          },
        },
      });

      try {
        await client.queryRag('Spam query');
        expect.unreachable('Should have thrown RateLimitError');
      } catch (err) {
        expect(err).toBeInstanceOf(RateLimitError);
        const rateLimitErr = err as RateLimitError;
        expect(rateLimitErr.isRateLimited).toBe(true);
        expect(rateLimitErr.retryAfterSeconds).toBe(45);
        expect(rateLimitErr.message).toContain('Rate limit exceeded');
      }
    });
  });

  describe('getAuditMetrics', () => {
    it('fetches total token and cost metrics', async () => {
      const mockMetrics: AuditMetrics = {
        totalQueries: 50,
        totalTokens: 12000,
        totalCostUsd: 0.0042,
        cacheHitRatio: 0.65,
      };

      vi.spyOn(client.axiosInstance, 'get').mockResolvedValueOnce({
        status: 200,
        data: { success: true, data: mockMetrics },
      });

      const metrics = await client.getAuditMetrics();
      expect(metrics).toEqual(mockMetrics);
    });
  });
});
