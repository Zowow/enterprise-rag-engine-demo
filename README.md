# Antigravity AI-Native Engineering Workflow Kit

An enterprise-grade, portable AI development kit designed for modern software teams building with LLM coding agents. Built for **separated frontend & backend monorepos**, featuring a **3-Role Agent Hierarchy**, **Docker container isolation**, **reproduction-first bug triage**, and **Playwright end-to-end verification**.

---

## 🚀 Quickstart: Drop into Any New or Existing Project

To use this workflow in a new or existing repository, simply copy the `templates/` and `.agents/` directories:

```bash
# 1. Copy the core specification templates to your project
cp -r templates/docs/ my-project/docs/
cp templates/docker/* my-project/
cp -r templates/e2e/ my-project/e2e/

# 2. Copy the active agent rules and skills
cp -r .agents/ my-project/.agents/
cp WORKFLOW.md my-project/WORKFLOW.md
```

Once copied, your IDE (Antigravity IDE, Cursor, Claude Code, Windsurf) immediately recognizes the slash commands and agent governance rules.

---

## 🏗️ Repository Architecture (Separated Frontend + Backend)

```
my-project/
├── WORKFLOW.md                            <-- "What do I do next?" decision tree & cheatsheet
├── docs/                                  <-- Unified Documentation
│   ├── ARCHITECTURE.md                    <-- Living Tech Specs (Backend + Frontend + API contracts)
│   ├── DESIGN_SYSTEM.md                   <-- Living UI Style DNA (colors, typography, tokens)
│   └── prds/                              <-- Feature PRD archive (001-auth.md, 002-billing.md)
│
├── frontend/                              <-- Web / Mobile App (Next.js, React, Vue, Flutter)
│   ├── src/
│   ├── Dockerfile.dev
│   └── package.json
│
├── backend/                               <-- API / Services (Node, FastAPI, Go, Rails)
│   ├── src/
│   ├── tests/
│   ├── Dockerfile.dev
│   └── requirements.txt / package.json
│
├── e2e/                                   <-- Playwright E2E Test Suite
│   ├── tests/
│   ├── artifacts/                         <-- Screenshots & failure traces
│   └── playwright.config.ts
│
├── docker-compose.dev.yml                 <-- Orchestrates: backend, frontend, db, playwright
└── .agents/                               <-- AI Engine (Rules & Skills)
    ├── rules/                             <-- Planning gate, Docker execution, Regression guard
    └── skills/                            <-- Slash commands (/prd, /milestone, /build, /verify, /fix, /change, /pr)
```

---

## 🤖 The 3-Role Agent Hierarchy

Instead of a single confused chatbot, responsibilities are cleanly divided across 3 specialized roles:

1. **The Architect Agent (`/prd`, `/milestone`, `/change`, `/pr`):**
   * **Mode:** Read-only planning, discovery interviews, system architecture, task decomposition.
   * **Boundary:** Does not edit code files directly. Generates plans for human approval.

2. **The Builder Agent (`/build`, `/fix`):**
   * **Mode:** Execution within Docker containers.
   * **Boundary:** Implements **one atomic milestone at a time** (<5 files, <10 mins). Follows strict Test-Driven Development (TDD).

3. **The QA / Verifier Agent (`/verify`):**
   * **Mode:** Browser automation and visual auditing.
   * **Boundary:** Runs headless Playwright in Docker, captures screenshots, inspects console logs, and feeds failure traces back to the Builder.

---

## ⚡ Available Slash Commands

| Slash Command | Persona | Purpose |
| :--- | :--- | :--- |
| **`/prd [idea]`** | Architect | Turns raw user stories into a structured, gated PRD in `docs/prds/`. |
| **`/milestone [prd-id]`** | Architect | Breaks an approved PRD into service-separated, atomic milestones. |
| **`/build [milestone #]`** | Builder | Checks out git branch and implements a milestone via TDD inside Docker. |
| **`/verify`** | QA | Runs full Playwright E2E suite in Docker; outputs UI screenshots. |
| **`/fix [bug description]`** | QA ➔ Builder | Writes failing reproduction test first, applies minimal fix, runs full regression check. |
| **`/change [prd-id]`** | Architect | Performs delta analysis for in-flight scope changes; revises milestones without losing work. |
| **`/pr`** | Architect | Generates clean GitHub Pull Request description with test proofs and screenshots. |
| **`/ship [prd-id]`** | Orchestrator | Macro autonomous pipeline (Build ➔ Verify ➔ Auto-heal ➔ Human review). |

For daily step-by-step guidance, refer directly to [WORKFLOW.md](WORKFLOW.md).
