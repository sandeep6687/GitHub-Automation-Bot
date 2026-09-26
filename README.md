# GitHub Automation Bot

An event-driven GitHub automation bot built as a modular monolith. Users authenticate via GitHub OAuth, connect repositories, and define custom automation rules. The bot ingests webhook events, runs rule evaluations, writes back to GitHub (labels, comments), and sends alerts to Slack.

---

## Architecture Overview

Built strictly adhering to the architectural constraints in [`AGENTS.md`](./AGENTS.md):

```
GitHubBot.Domain          → references: nothing (0 external dependencies)
GitHubBot.Application     → references: Domain
GitHubBot.Infrastructure  → references: Domain, Application
GitHubBot.Api             → references: Application, Infrastructure
```

- **Domain**: Pure business logic (RuleEngine, RetryCalculator), entities, value objects, repository interfaces. Zero NuGet packages.
- **Application**: Application services, DTOs, `IActionHandler` interface, `ActionDispatcher`.
- **Infrastructure**: EF Core 8 DbContext, PostgreSQL repositories, external API clients (GitHub, Slack), concrete action handlers, `BackgroundService` worker, AES-256-GCM encryption.
- **Api**: Thin controllers, middleware, global exception handling, JWT authentication, DI container setup.
- **UnitTests**: Fast unit tests (RuleEngine, RetryCalculator, architectural layer verification, signature validation).
- **IntegrationTests**: Webhook ingestion and worker claim/release integration tests.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 8, ASP.NET Core, C# 12 |
| ORM | Entity Framework Core 8 |
| Database | PostgreSQL (Neon free tier / local Docker) |
| Frontend | React 18, Vite 5, TypeScript |
| Testing | xUnit, Moq, FluentAssertions |
| Resilience | Polly |

---

## Getting Started

### Prerequisites

- [.NET 8 or 9 SDK](https://dotnet.microsoft.com/download)
- [Docker & Docker Compose](https://www.docker.com/) (optional, for local PostgreSQL)

### Clone & Build

```bash
# Clone the repository
git clone <repo-url>
cd Abstrabit

# Build the entire solution
dotnet build

# Run all tests (including architecture boundary tests)
dotnet test
```

### Running Locally with Docker Compose

```bash
cp .env.example .env
# Edit .env with your credentials
docker-compose up -d db
```

---

## Implementation Roadmap

- [x] **Phase 0**: Foundation (Solution structure, project references, AGENTS.md, config skeleton, build & test verification)
- [x] **Phase 1**: Database & Domain (Entities, EF Core DbContext, migrations, repository interfaces)
- [x] **Phase 2**: GitHub OAuth (Authentication flow, JWT cookie issuance)
- [x] **Phase 3**: Repository Connection (Repo listing, webhook secret generation & registration)
- [x] **Phase 4**: Webhook Ingestion (HMAC validation, deduplication, 202 Accepted)
- [x] **Phase 5**: Background Worker (Claim/release pattern, resilient processing)
- [x] **Phase 6**: Rule Engine (TitleContains, AuthorEquals, LabelContains)
- [x] **Phase 7**: GitHub Actions (Labeling, commenting, action-level idempotency)
- [x] **Phase 8**: Slack Notifications (Incoming Webhooks, templating, partial failure retry)
- [x] **Phase 9**: Dashboard & User-Facing API (React 18 + Vite, Rule CRUD, Activity Feed)
- [ ] **Phase 10**: End-to-End Verification & Production Deployment

---

## Dashboard & User-Facing API (Phase 9)

### 1. Overview
The dashboard allows authenticated GitHub users to:
1. Sign in via GitHub OAuth (session secured with HttpOnly, SameSite cookies).
2. View available GitHub repositories and connect them with automated webhook registration.
3. Manage connected repositories and view their rules and live activity.
4. Build automation rules with multi-condition matching (`TitleContains`, `AuthorEquals`, `LabelContains`).
5. Configure sequential actions (`AddLabel`, `AddComment`, `SlackNotify`) with safe template variables.
6. Inspect real-time webhook event processing, attempt counts, and individual action executions with status pills.

### 2. Running Locally

#### Prerequisites
- .NET 8 SDK
- Node.js v18+ (tested on Node v24)
- PostgreSQL 16+ running locally or in Docker

#### Backend API Run
```bash
# Set required secrets (placeholders shown; set real secrets in your local shell)
export GITHUB_CLIENT_ID="your_github_client_id"
export GITHUB_CLIENT_SECRET="your_github_client_secret"
export ENCRYPTION_KEY="your_32_byte_base64_encryption_key"
export Slack__WebhookUrl="https://hooks.slack.com/services/T00/B00/XXXX"

# Run backend API & background worker on http://localhost:5000
dotnet run --project src/GitHubBot.Api
```

#### Frontend Dashboard Run
```bash
# Navigate to frontend and install dependencies
cd frontend
npm install

# Run Vite dev server on http://localhost:5173
npm run dev
```

During development, requests to `/api` are automatically proxied by Vite to `http://localhost:5000`.

### 3. API Surface

| Endpoint | Method | Description |
| :--- | :--- | :--- |
| `/api/auth/login` | GET | Initiates GitHub OAuth authentication |
| `/api/auth/callback` | GET | GitHub OAuth callback; sets HttpOnly session cookie |
| `/api/auth/me` | GET | Returns authenticated user profile |
| `/api/auth/logout` | POST | Terminates session cookie |
| `/api/repositories/available` | GET | Lists authorized GitHub repositories |
| `/api/repositories` | GET | Lists connected repositories for the current user |
| `/api/repositories/connect` | POST | Connects repository, generates secret, registers GitHub webhook |
| `/api/repositories/{id}` | DELETE | Disconnects repository and removes webhook |
| `/api/repositories/{id}/rules` | GET | Lists configured automation rules |
| `/api/repositories/{id}/rules` | POST | Creates a rule with conditions and actions |
| `/api/repositories/{id}/rules/{ruleId}` | GET | Gets a single rule by ID |
| `/api/repositories/{id}/rules/{ruleId}` | PUT | Updates a rule, conditions, and actions |
| `/api/repositories/{id}/rules/{ruleId}/enabled` | PATCH | Enables or disables an automation rule |
| `/api/repositories/{id}/rules/{ruleId}` | DELETE | Deletes a rule and cascades its conditions/actions |
| `/api/repositories/{id}/activity` | GET | Bounded query for event history & action executions |

### 4. Rule Configuration & Template Variables
- **Conditions**: Evaluated with strict `AND` logic (`TitleContains`, `AuthorEquals`, `LabelContains`).
- **Actions**: `AddLabel` (`label`), `AddComment` (`body`), `SlackNotify` (`message`).
- **Variables**: `{{title}}`, `{{author}}`, `{{action}}`, `{{repository}}`, `{{event}}`, `{{issueNumber}}`, `{{url}}`.

---

## Slack Notifications (Phase 8)

### 1. Slack Setup & Incoming Webhook
1. Go to [Slack API: Your Apps](https://api.slack.com/apps) and create a new Slack App in your workspace.
2. Under **Features**, select **Incoming Webhooks** and switch the toggle to **Activate Incoming Webhooks**.
3. Click **Add New Webhook to Workspace**, select the target channel (e.g. `#alerts`), and authorize.
4. Copy the generated Webhook URL (format: `https://hooks.slack.com/services/T00/B00/XXXX`).

### 2. Environment Variable Configuration
Configure the webhook URL as a server-side secret using either environment variables or `appsettings.json`:

```bash
# In .env or system environment
Slack__WebhookUrl=https://hooks.slack.com/services/T00000000/B00000000/XXXXXXXXXXXXXXXXXXXXXXXX
```

*Note: If `Slack__WebhookUrl` is not configured, the application starts normally. Only rules triggering `SlackNotify` will fail gracefully with a descriptive configuration error.*

### 3. Reliability & Best-Effort Delivery Semantics
- **At-least-once event processing with best-effort external action idempotency.**
- **No exactly-once guarantee**: Incoming Webhooks do not support distributed two-phase commit. A crash after Slack accepts the message but before `ActionExecution.Success` is saved in PostgreSQL can theoretically result in a duplicate notification on retry.
- **Action-level deduplication**: On event retry, `ActionDispatcher` skips actions whose `ActionExecution` status is already `SUCCESS`.
- **Partial Failure Handling**: If GitHub `AddLabel` succeeds and `SlackNotify` fails transiently (e.g. 429 or 5xx), only `SlackNotify` is retried on subsequent worker claims.

### 4. Automated Testing Instructions
```bash
# Run all unit tests (190 tests)
dotnet test tests/GitHubBot.UnitTests/GitHubBot.UnitTests.csproj --nologo

# Run real PostgreSQL integration tests (51 tests)
dotnet test tests/GitHubBot.IntegrationTests/GitHubBot.IntegrationTests.csproj --nologo

# Build frontend production bundle
cd frontend && npm run build
```
