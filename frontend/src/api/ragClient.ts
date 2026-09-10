import axios, { AxiosInstance, AxiosError } from 'axios';
import type {
  ApiResponse,
  DocumentItem,
  DocumentUploadResult,
  RagQueryResult,
  AuditMetrics,
  RagQueryResponseWithMeta,
  RateLimitInfo,
} from '../types/rag';

export class RateLimitError extends Error implements RateLimitInfo {
  public readonly isRateLimited = true;
  public readonly retryAfterSeconds: number;
  public readonly limit?: number;
  public readonly remaining?: number;

  constructor(
    message: string,
    retryAfterSeconds: number = 60,
    limit?: number,
    remaining?: number
  ) {
    super(message);
    this.name = 'RateLimitError';
    this.retryAfterSeconds = retryAfterSeconds;
    this.limit = limit;
    this.remaining = remaining;
  }
}

export class RagClient {
  public readonly axiosInstance: AxiosInstance;

  constructor(baseURL?: string) {
    const defaultUrl =
      typeof import.meta !== 'undefined' && import.meta.env?.VITE_API_URL
        ? import.meta.env.VITE_API_URL
        : 'http://localhost:5000';

    this.axiosInstance = axios.create({
      baseURL: baseURL || defaultUrl,
      headers: {
        'Content-Type': 'application/json',
      },
    });

    // Interceptor to uniformly handle HTTP 429 Rate Limiting
    this.axiosInstance.interceptors.response.use(
      (response) => response,
      (error: AxiosError<{ success?: boolean; error?: string }>) => {
        if (error.response?.status === 429) {
          const retryHeader = error.response.headers?.['retry-after'];
          const retryAfterSeconds = retryHeader ? parseInt(String(retryHeader), 10) : 60;
          const limitHeader = error.response.headers?.['x-ratelimit-limit'];
          const remainingHeader = error.response.headers?.['x-ratelimit-remaining'];
          const errorMessage =
            error.response.data?.error ||
            'Rate limit exceeded. Please wait before submitting more queries.';

          return Promise.reject(
            new RateLimitError(
              errorMessage,
              isNaN(retryAfterSeconds) ? 60 : retryAfterSeconds,
              limitHeader ? parseInt(String(limitHeader), 10) : undefined,
              remainingHeader ? parseInt(String(remainingHeader), 10) : undefined
            )
          );
        }

        const serverError = error.response?.data?.error;
        if (serverError) {
          return Promise.reject(new Error(serverError));
        }

        return Promise.reject(error);
      }
    );
  }

  /**
   * Upload a policy or compliance document to the ingestion pipeline
   */
  public async uploadDocument(
    file: File,
    title?: string
  ): Promise<DocumentUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    if (title) {
      formData.append('title', title);
    }

    try {
      const response = await this.axiosInstance.post<ApiResponse<DocumentUploadResult>>(
        '/api/v1/documents/upload',
        formData,
        {
          headers: {
            'Content-Type': 'multipart/form-data',
          },
        }
      );

      if (!response.data.success || !response.data.data) {
        throw new Error(response.data.error || 'Failed to upload document.');
      }

      return response.data.data;
    } catch (err: unknown) {
      if (err instanceof RateLimitError) {
        throw err;
      }
      const axiosErr = err as AxiosError<{ success?: boolean; error?: string }>;
      if (axiosErr.response?.status === 429) {
        const retryHeader = axiosErr.response.headers?.['retry-after'];
        const retryAfterSeconds = retryHeader ? parseInt(String(retryHeader), 10) : 60;
        const limitHeader = axiosErr.response.headers?.['x-ratelimit-limit'];
        const remainingHeader = axiosErr.response.headers?.['x-ratelimit-remaining'];
        const errorMessage =
          axiosErr.response.data?.error ||
          'Rate limit exceeded. Please wait before submitting more queries.';

        throw new RateLimitError(
          errorMessage,
          isNaN(retryAfterSeconds) ? 60 : retryAfterSeconds,
          limitHeader ? parseInt(String(limitHeader), 10) : undefined,
          remainingHeader ? parseInt(String(remainingHeader), 10) : undefined
        );
      }
      if (axiosErr.response?.data?.error) {
        throw new Error(axiosErr.response.data.error);
      }
      if (err instanceof Error) {
        throw err;
      }
      throw new Error('An unexpected error occurred during document upload.');
    }
  }

  /**
   * Fetch all uploaded documents with indexing status
   */
  public async getDocuments(): Promise<DocumentItem[]> {
    const response = await this.axiosInstance.get<ApiResponse<DocumentItem[]>>(
      '/api/v1/documents'
    );

    if (!response.data.success || !response.data.data) {
      throw new Error(response.data.error || 'Failed to fetch documents.');
    }

    return response.data.data;
  }

  /**
   * Fetch status and details of a single document
   */
  public async getDocumentById(id: string): Promise<DocumentItem> {
    const response = await this.axiosInstance.get<ApiResponse<DocumentItem>>(
      `/api/v1/documents/${id}`
    );

    if (!response.data.success || !response.data.data) {
      throw new Error(response.data.error || `Document '${id}' not found.`);
    }

    return response.data.data;
  }

  /**
   * Query the RAG engine for answers and verified citations
   */
  public async queryRag(query: string): Promise<RagQueryResponseWithMeta> {
    try {
      const response = await this.axiosInstance.post<ApiResponse<RagQueryResult>>(
        '/api/v1/query',
        { query }
      );

      if (!response.data.success || !response.data.data) {
        throw new Error(response.data.error || 'Failed to execute query.');
      }

      const xCacheHeader = response.headers?.['x-cache'];
      const isSemanticCacheHit =
        xCacheHeader === 'HIT-SEMANTIC' || response.data.data.isCached === true;

      const remainingHeader = response.headers?.['x-ratelimit-remaining'];
      const limitHeader = response.headers?.['x-ratelimit-limit'];

      return {
        result: response.data.data,
        isSemanticCacheHit,
        rateLimitRemaining: remainingHeader ? parseInt(String(remainingHeader), 10) : undefined,
        rateLimitLimit: limitHeader ? parseInt(String(limitHeader), 10) : undefined,
      };
    } catch (err: unknown) {
      if (err instanceof RateLimitError) {
        throw err;
      }
      const axiosErr = err as AxiosError<{ success?: boolean; error?: string }>;
      if (axiosErr.response?.status === 429) {
        const retryHeader = axiosErr.response.headers?.['retry-after'];
        const retryAfterSeconds = retryHeader ? parseInt(String(retryHeader), 10) : 60;
        const limitHeader = axiosErr.response.headers?.['x-ratelimit-limit'];
        const remainingHeader = axiosErr.response.headers?.['x-ratelimit-remaining'];
        const errorMessage =
          axiosErr.response.data?.error ||
          'Rate limit exceeded. Please wait before submitting more queries.';

        throw new RateLimitError(
          errorMessage,
          isNaN(retryAfterSeconds) ? 60 : retryAfterSeconds,
          limitHeader ? parseInt(String(limitHeader), 10) : undefined,
          remainingHeader ? parseInt(String(remainingHeader), 10) : undefined
        );
      }
      if (axiosErr.response?.data?.error) {
        throw new Error(axiosErr.response.data.error);
      }
      if (err instanceof Error) {
        throw err;
      }
      throw new Error('An unexpected error occurred during RAG query.');
    }
  }

  /**
   * Fetch system audit metrics (total queries, tokens, cost, cache ratio)
   */
  public async getAuditMetrics(): Promise<AuditMetrics> {
    const response = await this.axiosInstance.get<ApiResponse<AuditMetrics>>(
      '/api/v1/audit/metrics'
    );

    if (!response.data.success || !response.data.data) {
      throw new Error(response.data.error || 'Failed to fetch audit metrics.');
    }

    return response.data.data;
  }
}

export const ragClient = new RagClient();
export default ragClient;
