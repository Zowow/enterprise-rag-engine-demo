/**
 * Standard API Envelope matching ASP.NET Core controllers
 */
export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  error?: string;
}

/**
 * Status stages for document ingestion lifecycle
 */
export type IngestionStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed';

/**
 * Uploaded Document metadata model
 */
export interface DocumentItem {
  id: string;
  title: string;
  fileName: string;
  fileSizeBytes: number;
  status: IngestionStatus | string;
  failureReason?: string | null;
  chunkCount: number;
  createdAt: string;
  updatedAt?: string;
}

/**
 * Response payload from POST /api/v1/documents/upload
 */
export interface DocumentUploadResult {
  jobId: string;
  documentId: string;
  title: string;
  status: IngestionStatus | string;
}

/**
 * Source citation returned with RAG answer
 */
export interface Citation {
  documentTitle: string;
  title?: string;
  pageNumber: number;
  excerpt: string;
}

/**
 * Request payload for POST /api/v1/query
 */
export interface RagQueryRequest {
  query: string;
}

/**
 * Data payload returned for RAG query
 */
export interface RagQueryResult {
  answer: string;
  citations: Citation[];
  isCached: boolean;
  costUsd: number;
}

/**
 * Aggregated token usage and cost metrics from GET /api/v1/audit/metrics
 */
export interface AuditMetrics {
  totalQueries: number;
  totalTokens: number;
  totalCostUsd: number;
  cacheHitRatio: number;
}

/**
 * Real-time event emitted over SignalR hub /hubs/ingestion
 */
export interface IngestionProgressUpdate {
  jobId: string;
  status: IngestionStatus | string;
  progressPercentage: number;
  message: string;
  documentId?: string | null;
  timestamp?: string;
}

/**
 * Structured rate limit error payload for HTTP 429 responses
 */
export interface RateLimitInfo {
  isRateLimited: boolean;
  retryAfterSeconds: number;
  message: string;
  limit?: number;
  remaining?: number;
}

/**
 * Query response augmented with HTTP response metadata (cache status & rate limits)
 */
export interface RagQueryResponseWithMeta {
  result: RagQueryResult;
  isSemanticCacheHit: boolean;
  rateLimitRemaining?: number;
  rateLimitLimit?: number;
}
