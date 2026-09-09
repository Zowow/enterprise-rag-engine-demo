# PRD-001: Enterprise Knowledge RAG Engine

* **Status:** `[In Progress]`
* **Owner:** Principal Product Architect & Engineering Team
* **Target Release / Milestone:** v1.0.0-demo
* **Created Date:** 2026-09-09
* **Last Updated:** 2026-09-09

---

## 1. Executive Summary & Problem Statement

### The Problem
Enterprise teams struggle with slow, manual discovery across large legal and compliance document repositories. Standard naive RAG architectures suffer from slow synchronous upload bottlenecks, lack of ingestion progress visibility, high LLM API costs for repetitive queries, unreliable answers with hallucinated source references, and exposure to runaway OpenAI API billing or key quota exhaustion from unconstrained query volume.

### The Solution
A production-grade, asynchronous enterprise RAG engine built with ASP.NET Core 9, Microsoft Semantic Kernel, Qdrant vector database, Azurite cloud emulation, Redis semantic caching and rate limiting, and a modern React client. The platform enables drag-and-drop document ingestion with real-time SignalR progress tracking, zero-hallucination source citation verification, sub-30ms semantic cache retrieval for frequent compliance queries, and Redis-backed sliding window rate limiting to strictly safeguard OpenAI API quotas.

---

## 2. User Stories & Acceptance Criteria

### User Story 1: Asynchronous Document Upload & Queue Dispatch
> **As a** Compliance Analyst,  
> **I want to** upload policy documents (PDF, DOCX, MD up to 25MB) through a drag-and-drop interface and receive an immediate job acknowledgement,  
> **So that** my browser is not blocked by heavy parsing and vectorization operations.

#### Acceptance Criteria (Given / When / Then):
- [ ] **Scenario A (Happy Path Upload):**
  - **Given** a valid document file (`compliance-policy.pdf`, size $\le 25$MB),
  - **When** the user uploads the file via the React upload zone,
  - **Then** the API uploads the raw binary to Azurite Blob Storage (`documents` container), creates a record in PostgreSQL with status `Queued`, enqueues a message in Azurite Queue (`document-ingestion-queue`), and returns `202 Accepted` with a `jobId` and `documentId`.
- [ ] **Scenario B (File Validation Error):**
  - **Given** an unsupported file extension (e.g. `.exe`) or a file exceeding 25MB,
  - **When** the user attempts to upload the file,
  - **Then** the API rejects the request with `400 Bad Request` and a clear error message, and no blob or queue message is created.

---

### User Story 2: Background Ingestion Worker & Real-Time SignalR Feedback
> **As a** Compliance Analyst,  
> **I want to** watch the live ingestion progress of my uploaded document in real time without refreshing the page,  
> **So that** I know exactly when the document is chunked, embedded, and ready for queries.

#### Acceptance Criteria (Given / When / Then):
- [ ] **Scenario A (Happy Path Real-Time Progress):**
  - **Given** an ingestion job enqueued in Azurite Queue,
  - **When** the .NET background worker consumes the queue message,
  - **Then** it parses the document using `PdfPig` / `OpenXml`, chunks content into 500-token blocks (50-token overlap) with page numbers, generates 1536-dimensional embeddings via OpenAI `text-embedding-3-small`, upserts vectors into Qdrant collection `documents`, and broadcasts SignalR progress events (`Parsing 25%` $\rightarrow$ `Embedding 75%` $\rightarrow$ `Completed 100%`) to the client.
- [ ] **Scenario B (Corrupted / Empty Scanned Document):**
  - **Given** a scanned image-only PDF with fewer than 50 extractable characters,
  - **When** the worker processes the file,
  - **Then** it terminates processing gracefully, marks the document status as `Failed` with reason `"No extractable text found"`, broadcasts a `Failed` SignalR event, and does not crash or poison the queue.

---

### User Story 3: Verified RAG Retrieval with Source Citations
> **As a** Legal Auditor,  
> **I want to** ask questions and receive answers strictly grounded in uploaded documents accompanied by exact citations (document title, page number, and text excerpt),  
> **So that** I can instantly verify the authenticity of legal and compliance answers and avoid hallucinations.

#### Acceptance Criteria (Given / When / Then):
- [ ] **Scenario A (Relevant Query with Citations):**
  - **Given** indexed documents in Qdrant,
  - **When** the user submits a natural language question via the React chat interface,
  - **Then** the API retrieves the top matching chunks ($k=4$) from Qdrant, invokes Semantic Kernel with `gpt-4o-mini` using a strict grounding prompt, and returns a structured JSON response containing `markdownAnswer` and a `citations` array with `{ documentTitle, pageNumber, excerpt }`.
- [ ] **Scenario B (No Relevant Documentation Found):**
  - **Given** a query whose cosine similarity score falls below the retrieval threshold (< 0.70) or matches zero chunks,
  - **When** the query is evaluated,
  - **Then** the system returns `"No relevant documentation found to answer this question."` with an empty citations array.

---

### User Story 4: Semantic Query Caching & Cost Auditing
> **As a** Platform Operations Engineer,  
> **I want to** serve semantically identical queries directly from Redis cache and log token cost metrics to PostgreSQL,  
> **So that** repeated questions cost $0 in LLM fees and respond within sub-30ms latencies.

#### Acceptance Criteria (Given / When / Then):
- [ ] **Scenario A (Semantic Cache Hit):**
  - **Given** a query semantically equivalent ($\ge 0.95$ cosine similarity) to a previously cached query embedding in Redis,
  - **When** the API processes the query,
  - **Then** it short-circuits LLM synthesis, returns the cached answer and citations with HTTP header `X-Cache: HIT-SEMANTIC` in $< 30$ms, and logs a cache hit to PostgreSQL audit logs with prompt/completion token cost of $0.00.
- [ ] **Scenario B (Semantic Cache Miss):**
  - **Given** a new or distinct query (< 0.95 cosine similarity),
  - **When** the API executes RAG synthesis,
  - **Then** it caches the query embedding, answer, and citations in Redis with a 24-hour TTL, returns `X-Cache: MISS`, and records the exact input/output tokens and estimated cost in PostgreSQL `audit_logs`.

---

### User Story 5: API Rate Limiting & OpenAI Cost Protection
> **As a** Platform Administrator,  
> **I want to** enforce sliding-window rate limits backed by Redis on RAG query and upload endpoints,  
> **So that** our OpenAI API key quota is strictly protected against accidental infinite loops, rapid spamming, and budget overruns.

#### Acceptance Criteria (Given / When / Then):
- [ ] **Scenario A (Within Normal Request Limits):**
  - **Given** a client issuing queries within configured thresholds (default: 20 queries/minute per client IP),
  - **When** the request reaches the ASP.NET Core rate-limiting middleware,
  - **Then** the request is allowed to proceed and includes standard response headers `X-RateLimit-Limit` and `X-RateLimit-Remaining`.
- [ ] **Scenario B (Rate Limit Exceeded / HTTP 429):**
  - **Given** a client that exceeds 20 requests within the 60-second sliding window,
  - **When** an additional request is dispatched,
  - **Then** the API immediately short-circuits the pipeline, blocks LLM execution, and returns `429 Too Many Requests` with header `Retry-After: <seconds>` and body `{ success: false, error: "Rate limit exceeded. Please wait before submitting more queries." }`.
- [ ] **Scenario C (Client-Side Rate Limit UX):**
  - **Given** an HTTP 429 response from the API,
  - **When** received by the React frontend,
  - **Then** the query submit button is temporarily disabled, and an alert banner displaying the remaining countdown retry time is shown to the user.

---

## 3. Scope Boundaries

### In-Scope (v1):
* Asynchronous document upload pipeline (PDF, DOCX, MD up to 25MB).
* Azurite Blob Storage for raw file storage and Azurite Queue for job distribution.
* .NET 9 Background Ingestion Worker with `PdfPig` and `DocumentFormat.OpenXml`.
* Sliding window chunking (500 tokens, 50-token overlap, page-level tracking).
* OpenAI `text-embedding-3-small` (1536 dims) and `gpt-4o-mini` synthesis via Microsoft Semantic Kernel.
* Qdrant vector database (Dockerized) with HNSW indexing and metadata payloads.
* Redis (Dockerized) semantic query caching with $\ge 0.95$ cosine threshold and 24-hour TTL.
* Redis-backed sliding window rate limiting (protecting OpenAI API budget with HTTP 429 & `Retry-After`).
* ASP.NET Core SignalR hub broadcasting real-time ingestion status.
* PostgreSQL database via EF Core for document metadata and token usage audit logs.
* Streamlined React client (Vite + TypeScript + Tailwind CSS) with upload dropzone, live progress bar, chat interface, citation inspection drawer, and rate-limit countdown alert.
* Single-command local orchestration via `docker-compose.yml`.

### Out-of-Scope (Deferred to Future PRDs):
* ❌ Role-Based Access Control (RBAC) and user authentication (streamlined pure RAG demo).
* ❌ OCR for scanned/image-only PDFs (Tesseract/Azure Vision).
* ❌ Multi-turn conversational memory / chat sessions (v1 focuses on verified single-turn Q&A).
* ❌ Document deletion, versioning, and re-indexing workflows.
* ❌ Multi-tenant database isolation.

---

## 4. Technical Architecture & Data Models

### Database Schema Additions / Migrations:
```sql
-- PostgreSQL via EF Core

CREATE TABLE documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(255) NOT NULL,
    file_name VARCHAR(255) NOT NULL,
    file_size_bytes BIGINT NOT NULL,
    content_type VARCHAR(100) NOT NULL,
    blob_uri TEXT NOT NULL,
    status VARCHAR(50) NOT NULL, -- Queued, Processing, Completed, Failed
    failure_reason TEXT NULL,
    chunk_count INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE TABLE audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    query_text TEXT NOT NULL,
    response_cached BOOLEAN NOT NULL DEFAULT FALSE,
    cache_score DOUBLE PRECISION NULL,
    prompt_tokens INT NOT NULL DEFAULT 0,
    completion_tokens INT NOT NULL DEFAULT 0,
    total_tokens INT NOT NULL DEFAULT 0,
    estimated_cost_usd NUMERIC(10, 6) NOT NULL DEFAULT 0.000000,
    execution_duration_ms INT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

CREATE INDEX idx_documents_status ON documents(status);
CREATE INDEX idx_audit_logs_created_at ON audit_logs(created_at DESC);
```

### Rate Limiting Specification (Redis-backed):
* **Algorithm:** Sliding Window Counter.
* **Key Format:** `ratelimit:{endpoint}:{client_ip}`
* **Default Limits:**
  * Query endpoint (`POST /api/v1/query`): 20 requests per 60-second window.
  * Upload endpoint (`POST /api/v1/documents/upload`): 5 requests per 60-second window.
* **Headers Emitted:** `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `Retry-After` (on 429).

### Vector Database Payload Schema (Qdrant Collection: `documents`):
```json
{
  "vector": [1536-dimensional float array],
  "payload": {
    "docId": "uuid-string",
    "title": "compliance-manual.pdf",
    "chunkIndex": 12,
    "pageNumber": 4,
    "text": "Extracted paragraph content..."
  }
}
```

### API Endpoints:
| Method | Route | Description | Request Body | Response Envelope | Rate Limited |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `POST` | `/api/v1/documents/upload` | Upload document file | `multipart/form-data` (`file`) | `{ success: true, data: { jobId, documentId, title, status } }` | Yes (5/min) |
| `GET` | `/api/v1/documents` | List uploaded documents | None | `{ success: true, data: [ { id, title, status, chunkCount, createdAt } ] }` | No |
| `GET` | `/api/v1/documents/:id` | Get document status | None | `{ success: true, data: { id, title, status, failureReason } }` | No |
| `POST` | `/api/v1/query` | Ask question to RAG engine | `{ query: "string" }` | `{ success: true, data: { answer: "markdown", citations: [ { title, pageNumber, excerpt } ], isCached: boolean, costUsd: number } }` | Yes (20/min) |
| `GET` | `/api/v1/audit/metrics` | Retrieve total tokens & cost | None | `{ success: true, data: { totalQueries, totalTokens, totalCostUsd, cacheHitRatio } }` | No |

### Real-time SignalR Hub (`/hubs/ingestion`):
* Client joins group: `jobId`
* Server emits: `IngestionProgress(jobId, status, progressPercentage, message)`

---

## 5. UI / UX Specifications & `data-testid` Requirements

* **Page Layout:** Single-page dashboard divided into two responsive columns:
  * **Left Column:** Document ingestion management & document index table.
  * **Right Column:** RAG Q&A interface with citation preview drawer & token metrics ribbon.
* **Key Components & Test IDs:**
  * Document Upload Dropzone: `data-testid="document-upload-dropzone"`
  * Document File Input: `data-testid="document-file-input"`
  * Upload Submit Button: `data-testid="document-upload-button"`
  * Live Ingestion Progress Bar: `data-testid="ingestion-progress-bar"`
  * Ingestion Status Badge: `data-testid="ingestion-status-badge"`
  * Query Input Box: `data-testid="rag-query-input"`
  * Query Submit Button: `data-testid="rag-query-submit"`
  * Rate Limit Countdown Banner: `data-testid="rate-limit-banner"`
  * Answer Markdown Container: `data-testid="rag-answer-container"`
  * Citation Pill / Badge: `data-testid="citation-pill"`
  * Citation Drawer Excerpt: `data-testid="citation-drawer-excerpt"`
  * Cache Hit Indicator Badge: `data-testid="cache-status-badge"`
  * Cost & Token Metrics Ribbon: `data-testid="token-metrics-ribbon"`
* **Design System Reference:** Follow tokens in `docs/DESIGN_SYSTEM.md` (clean modern dark/light palette, sans-serif typography, rounded cards).

---

## 6. Change Addendum (Use only for in-flight changes)

*None at creation.*
