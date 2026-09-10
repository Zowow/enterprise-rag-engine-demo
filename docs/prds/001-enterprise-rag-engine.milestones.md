# Milestones: PRD-001 - Enterprise Knowledge RAG Engine

> **THE ONE-SHOT VERIFIABLE RULE:**  
> Every milestone must touch **fewer than 5 files**, take **under 10 minutes** for an agent to build, and feature a deterministic **verification command** executed inside Docker.

---

## 🚦 Execution Status Tracker

- **Overall Progress:** `[ 14 / 14 Milestones Completed ]`
- **Target Branch:** `feat/prd-001-enterprise-rag-engine`

---

## 📦 Phase 1: Container Orchestration & Core Infrastructure

### [x] Milestone 1: Containerized Development Environment
* **Scope:** Configure Docker Compose development orchestration for .NET 9 Web API, background ingestion worker, React frontend, PostgreSQL, Redis, Qdrant, Azurite, and Playwright.
* **Target Files:**
  * `docker-compose.dev.yml`
  * `backend/Dockerfile.dev`
  * `worker/Dockerfile.dev`
  * `frontend/Dockerfile.dev`
* **Verification Command:**
  ```bash
  docker compose -f docker-compose.dev.yml config
  ```
* **Success Criteria:** Docker compose configuration validates without schema errors and defines all necessary service dependencies, volumes, and ports.

### [x] Milestone 2: PostgreSQL Schema Entities & EF Core Migrations
* **Scope:** Implement EF Core DbContext, entity models for `Document` and `AuditLog`, and initial migration scripts.
* **Target Files:**
  * `backend/src/Infrastructure/Data/AppDbContext.cs`
  * `backend/src/Domain/Entities/Document.cs`
  * `backend/src/Domain/Entities/AuditLog.cs`
  * `backend/tests/EnterpriseRAG.UnitTests/DatabaseTests.cs`
* **Verification Command:**
  ```bash
  docker compose exec backend dotnet test --filter FullyQualifiedName~DatabaseTests
  ```
* **Success Criteria:** Unit tests pass against PostgreSQL/in-memory EF Core asserting schema constraints, indexes, and entity state transitions.

---

## ⚙️ Phase 2: Ingestion Pipeline & Real-Time Feedback

### [x] Milestone 3: Document Upload API & Azurite Queue Dispatch
* **Scope:** Implement upload endpoint (`POST /api/v1/documents/upload`) with file validation ($\le 25$MB, PDF/DOCX/MD), Azurite Blob Storage upload, and queue message dispatch.
* **Target Files:**
  * `backend/src/Api/Controllers/DocumentsController.cs`
  * `backend/src/Application/Services/DocumentUploadService.cs`
  * `backend/src/Infrastructure/Storage/AzureBlobQueueService.cs`
  * `backend/tests/EnterpriseRAG.UnitTests/DocumentUploadTests.cs`
* **Verification Command:**
  ```bash
  docker compose exec backend dotnet test --filter FullyQualifiedName~DocumentUploadTests
  ```
* **Success Criteria:** Unit tests verify valid file upload returns `202 Accepted` with `jobId`, invalid extensions/sizes return `400 Bad Request`, and Azurite blob/queue calls execute correctly.

### [x] Milestone 4: SignalR Ingestion Hub & Progress Broadcasting
* **Scope:** Implement ASP.NET Core SignalR hub (`/hubs/ingestion`) and notification service to broadcast real-time ingestion state (`Parsing`, `Embedding`, `Completed`, `Failed`).
* **Target Files:**
  * `backend/src/Api/Hubs/IngestionHub.cs`
  * `backend/src/Application/Common/Interfaces/IIngestionNotifier.cs`
  * `backend/src/Infrastructure/Notifications/SignalRIngestionNotifier.cs`
  * `backend/tests/EnterpriseRAG.UnitTests/SignalRNotifierTests.cs`
* **Verification Command:**
  ```bash
  docker compose exec backend dotnet test --filter FullyQualifiedName~SignalRNotifierTests
  ```
* **Success Criteria:** Unit tests verify clients can join job-specific groups and receive strongly typed progress payloads.

### [x] Milestone 5: Background Ingestion Worker & Qdrant Vector Upsert
* **Scope:** Build the .NET 9 background queue worker: listen to Azurite Queue, extract text via `PdfPig` / `OpenXml`, chunk text into 500-token blocks with page tracking, generate embeddings, and upsert vectors into Qdrant collection `documents` (with empty-document failure handling).
* **Target Files:**
  * `worker/src/IngestionWorker.cs`
  * `worker/src/Parsers/DocumentTextExtractor.cs`
  * `worker/src/Chunking/TokenTextChunker.cs`
  * `worker/src/VectorStore/QdrantIndexer.cs`
  * `worker/tests/EnterpriseRAG.WorkerTests/IngestionWorkerTests.cs`
* **Verification Command:**
  ```bash
  docker compose exec worker dotnet test --filter FullyQualifiedName~IngestionWorkerTests
  ```
* **Success Criteria:** Worker parses sample PDF and DOCX, emits SignalR progress steps, upserts vectors to Qdrant, and marks scanned/empty documents as `Failed`.

---

## 🧠 Phase 3: RAG Retrieval, Semantic Caching & Rate Limiting

### [x] Milestone 6: Redis Sliding-Window Rate Limiting Middleware
* **Scope:** Implement ASP.NET Core rate limiting middleware backed by Redis, enforcing 20 queries/min and 5 uploads/min per IP with HTTP `429 Too Many Requests` and `Retry-After` headers.
* **Target Files:**
  * `backend/src/Api/Middleware/RedisRateLimitingMiddleware.cs`
  * `backend/src/Infrastructure/Cache/RedisRateLimiter.cs`
  * `backend/tests/EnterpriseRAG.UnitTests/RateLimitingTests.cs`
* **Verification Command:**
  ```bash
  docker compose exec backend dotnet test --filter FullyQualifiedName~RateLimitingTests
  ```
* **Success Criteria:** Requests under threshold pass with `X-RateLimit` headers; exceeding limit returns 429 with `Retry-After` and JSON error envelope.

### [x] Milestone 7: RAG Retrieval, Semantic Kernel Synthesis & Citation Mapping
* **Scope:** Implement `POST /api/v1/query` endpoint: perform vector retrieval against Qdrant, synthesize responses via Microsoft Semantic Kernel (`gpt-4o-mini`), format source citations, and persist token usage to `audit_logs`.
* **Target Files:**
  * `backend/src/Api/Controllers/QueryController.cs`
  * `backend/src/Application/Services/RagQueryService.cs`
  * `backend/src/Infrastructure/AI/SemanticKernelOrchestrator.cs`
  * `backend/tests/EnterpriseRAG.UnitTests/RagQueryTests.cs`
* **Verification Command:**
  ```bash
  docker compose exec backend dotnet test --filter FullyQualifiedName~RagQueryTests
  ```
* **Success Criteria:** Queries retrieve top Qdrant chunks, synthesize markdown answers with citations `{ documentTitle, pageNumber, excerpt }`, and insert token cost rows in PostgreSQL.

### [x] Milestone 8: Redis Semantic Query Caching
* **Scope:** Implement semantic caching layer: compute query embedding, check Redis vector similarity ($\ge 0.95$ threshold), return cached answer in $< 30$ms with `X-Cache: HIT-SEMANTIC`, and write new queries on miss with 24-hour TTL.
* **Target Files:**
  * `backend/src/Application/Services/SemanticCacheService.cs`
  * `backend/src/Infrastructure/Cache/RedisSemanticCache.cs`
  * `backend/tests/EnterpriseRAG.UnitTests/SemanticCacheTests.cs`
* **Verification Command:**
  ```bash
  docker compose exec backend dotnet test --filter FullyQualifiedName~SemanticCacheTests
  ```
* **Success Criteria:** Similar queries hit Redis semantic cache without invoking LLM, verify `X-Cache: HIT-SEMANTIC` header, and record $0.00 token cost.

---

## 🌐 Phase 4: Shared Contracts & React Client UI

### [x] Milestone 9: API Client SDK, TypeScript Types & SignalR Hook
* **Scope:** Define shared TypeScript interfaces matching backend response envelopes, implement Axios API client, and author custom React hook for SignalR connection management.
* **Target Files:**
  * `frontend/src/types/rag.ts`
  * `frontend/src/api/ragClient.ts`
  * `frontend/src/hooks/useIngestionSignalR.ts`
* **Verification Command:**
  ```bash
  docker compose exec frontend npm run typecheck
  ```
* **Success Criteria:** TypeScript compiles with 0 type errors across all API envelopes, models, and SignalR event payloads.

### [x] Milestone 10: Ingestion UI (Upload Dropzone, Real-time Progress & Document Table)
* **Scope:** Build React upload dropzone with client validation, animated SignalR progress bar, and document status table with required `data-testid` attributes.
* **Target Files:**
  * `frontend/src/components/DocumentUploadZone.tsx`
  * `frontend/src/components/IngestionProgressBar.tsx`
  * `frontend/src/components/DocumentListTable.tsx`
  * `frontend/src/tests/UploadComponents.test.tsx`
* **Verification Command:**
  ```bash
  docker compose exec frontend npm test -- UploadComponents
  ```
* **Success Criteria:** Component unit tests verify file selection, upload button triggers API, SignalR progress updates UI bar, and all test IDs are present.

### [x] Milestone 11: RAG Chat UI, Citation Drawer & Rate Limit Alert
* **Scope:** Build interactive query input, markdown answer viewer, clickable citation pills opening the slide-out citation drawer, cost metrics ribbon, and HTTP 429 rate limit countdown alert.
* **Target Files:**
  * `frontend/src/components/RagChatInterface.tsx`
  * `frontend/src/components/CitationDrawer.tsx`
  * `frontend/src/components/RateLimitBanner.tsx`
  * `frontend/src/components/TokenMetricsRibbon.tsx`
  * `frontend/src/tests/ChatComponents.test.tsx`
* **Verification Command:**
  ```bash
  docker compose exec frontend npm test -- ChatComponents
  ```
* **Success Criteria:** Chat sends query, renders answer, clicking citation opens drawer with excerpt, and 429 response displays countdown banner and disables input.

---

## 🧪 Phase 5: End-to-End Verification

### [x] Milestone 12: Playwright Automated E2E Pipeline in Docker
* **Scope:** Author complete headless Playwright E2E test verifying full user journey: document upload $\rightarrow$ SignalR progress bar updates $\rightarrow$ query execution $\rightarrow$ citation drawer inspection $\rightarrow$ rapid query rate limit 429 banner trigger.
* **Target Files:**
  * `e2e/tests/rag-pipeline.spec.ts`
  * `e2e/playwright.config.ts`
* **Verification Command:**
  ```bash
  docker compose exec playwright npx playwright test e2e/tests/rag-pipeline.spec.ts
  ```
* **Success Criteria:** Headless Playwright suite executes cleanly in Docker, DOM assertions pass, and UI validation screenshot is generated in `e2e/artifacts/`.

---

## 🎨 Phase 6: UI Design System & Style DNA Refactoring

### [x] Milestone 13: Frontend Style DNA Refactoring (Medango Theme)
* **Scope:** Refactor React frontend CSS, typography, components, and layouts to fully adopt the Medango Style DNA documented in `docs/DESIGN_SYSTEM.md`: light-canvas first (`#F7F8FA`), dark contrast bento anchor cards (`#181920`), pill-shaped segmented tabs & search bar, warm sunburst profile header, and smooth modern micro-interactions while maintaining 100% test compatibility and `data-testid` integrity.
* **Target Files:**
  * `frontend/index.html`
  * `frontend/src/index.css`
  * `frontend/src/App.tsx`
  * `frontend/src/tests/StyleDnaComponents.test.tsx`
* **Verification Command:**
  ```bash
  docker compose exec frontend npm test
  docker compose exec frontend npm run typecheck
  ```
* **Success Criteria:** All unit tests pass in Docker, TypeScript compilation passes with 0 errors, and Playwright E2E pipeline remains green.

---

## 🧹 Phase 7: UI Streamlining & Feature Pruning

### [x] Milestone 14: UI Streamlining & Unused Feature Pruning
* **Scope:** Prune unused mock features from `App.tsx` (Sarah Jenkins auditor profile card, dummy Qdrant/Redis toggle switches, non-functional navigation tabs, disconnected top-nav search pill). Relocate document search directly into `DocumentListTable` header for intuitive inline filtering. Preserve core Medango Style DNA (light `#F7F8FA` canvas, squircle cards, Google Fonts typography, royal blue accents, animated progress bars, live SignalR status, and TokenMetricsRibbon). Update unit tests in `StyleDnaComponents.test.tsx` to assert the streamlined UI without testing dead mock elements.
* **Target Files:**
  * `frontend/src/App.tsx`
  * `frontend/src/components/DocumentListTable.tsx`
  * `frontend/src/index.css`
  * `frontend/src/tests/StyleDnaComponents.test.tsx`
* **Verification Command:**
  ```bash
  docker compose exec frontend npm test
  docker compose exec frontend npm run typecheck
  docker compose exec playwright npx playwright test e2e/tests/rag-pipeline.spec.ts
  ```
* **Success Criteria:** All unit tests pass in Docker, TypeScript compiler reports 0 errors, Playwright E2E suite passes in Docker, and the UI layout is clean, responsive, and 100% functional with zero dead mock UI.

