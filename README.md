# Event-Driven GitHub Automation Bot

An event-driven, extensible GitHub automation system built on a modern ASP.NET Core & React stack. It seamlessly integrates with GitHub via OAuth and GitHub Apps, ingests webhooks at scale securely, evaluates dynamic user-defined automation rules, and performs automated actions like issue labeling, commenting, Slack notifications, and Gemini AI triage.

## Live Demo
[https://git-hub-automation-bot.vercel.app](https://git-hub-automation-bot.vercel.app)

## Problem
Maintaining open-source or enterprise repositories requires constant triage. Developers spend significant time labeling issues, routing notifications, and analyzing bugs. This bot aims to eliminate that toil through an intuitive, configurable, and highly resilient automated pipeline.

## Features
- **Secure Webhook Ingestion**: HMAC SHA-256 validation for GitHub webhooks.
- **Rule Engine**: Dynamic condition-to-action routing (`TitleContains`, `AuthorEquals`, `LabelContains`).
- **Resilient Execution**: Database-backed event queue with robust background workers, locking mechanisms, and exponential backoff.
- **Multi-tenant Isolation**: One user can safely connect multiple repositories with strict data isolation.
- **AI Triage**: Built-in integrations with Gemini to auto-summarize and triage issues.
- **Observability**: A rich React dashboard for real-time activity and retry visibility.

## Architecture
Built as a Modular Monolith adhering strictly to Clean Architecture principles.
See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for full diagrams and request flows.

## Event Flow
Events flow predictably through a transactional pipeline:
`PENDING` → `PROCESSING` → `SUCCESS` (or `RETRYING` → `FAILED`).
This guarantees at-least-once delivery semantics for external side effects.

## GitHub App Authentication
The system uses dual-layered authentication. It defaults to the user's OAuth access token. However, if the user installs the specific "Event Automation Bot" GitHub App, the system securely generates temporary JWT Installation Access Tokens, unlocking higher rate limits.

## Security
Encryption-at-rest is enforced for all stored tokens and secrets (AES-256-GCM). Webhooks are cryptographically verified in constant time. 
See [docs/SECURITY.md](docs/SECURITY.md) for a comprehensive breakdown.

## Reliability
Concurrency issues are solved using PostgreSQL `FOR UPDATE SKIP LOCKED`. Stale event recovery ensures no data drops if the application crashes mid-process.
See [docs/RELIABILITY.md](docs/RELIABILITY.md).

## Multi-Repository Support
The database model ensures every Rule and Event explicitly belongs to a single `ConnectedRepository`. Tenancy filters are forcefully applied to all queries.

## AI Triage
Configurable Gemini integration can be enabled per-rule. It includes fallback redundancy (switching from `gemini-3.7-flash` to `gemini-3.5-flash-lite` on quota errors).

## Observability
The React frontend surfaces detailed timelines for every event, isolating rule evaluation speeds from the latency of external API requests. 

## Local Development
Requires:
- .NET 8 SDK
- Node.js 20+
- PostgreSQL

## Environment Variables
See `.env.example` in the backend for required configuration keys. NEVER commit real secrets to source control.

## Database Setup
1. Create a local PostgreSQL database.
2. Update the connection string.
3. Run `dotnet ef database update` in the `backend/src/GitHubBot.Infrastructure` project, or set `Database:ApplyMigrationsOnStartup = true` in appsettings.

## Running Tests
Unit, Integration, and Architecture tests are included.
```bash
cd backend
dotnet test
```

## Deployment
- **Frontend**: Deployed to Vercel.
- **Backend API**: Deployed to Render.
- **Database**: Hosted on Neon.

## Demo Instructions
See [docs/DEMO.md](docs/DEMO.md) for step-by-step evaluation instructions.

## Project Structure
- `backend/src/GitHubBot.Domain`: Core entities, Rule Engine logic.
- `backend/src/GitHubBot.Application`: Orchestration, DTOs, Handlers.
- `backend/src/GitHubBot.Infrastructure`: EF Core, GitHub/Slack/Gemini API clients.
- `backend/src/GitHubBot.Api`: REST endpoints, Middleware.
- `frontend/`: Vite + React + TypeScript.

## Design Decisions
We prioritized PostgreSQL as our event queue to avoid the complexity and cost of Kafka/Redis on free-tier infrastructure. The `DeliveryId` provides idempotent safety.

## Trade-offs
Because we ensure at-least-once delivery, in rare edge cases (e.g., a crash right after a GitHub API succeeds but before DB commits), an action might be repeated. This is an accepted trade-off for simplicity and avoiding distributed transactions.

## Known Limitations
- GitHub App Webhooks are not yet enabled; repository-level webhooks are currently utilized.

## AI Collaboration
See [AI_NOTES.md](AI_NOTES.md) for a breakdown of human vs AI responsibilities during this project's development.
