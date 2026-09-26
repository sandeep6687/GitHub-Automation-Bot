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
- [ ] **Phase 9**: Activity Log & Dashboard API
- [ ] **Phase 10**: Frontend Dashboard (React + Vite)
- [ ] **Phase 11**: End-to-End Verification & Deployment

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

### 3. Example RuleAction Configuration
In rule definition:
```json
{
  "message": "🐛 Alert in {{repository}}: {{title}} by {{author}} (#{{issueNumber}}) — {{url}}",
  "channel": "#engineering"
}
```

### 4. Supported Template Variables
The template engine uses strict, safe variable substitution. Unknown variables (e.g. `{{custom}}`) remain unchanged without code execution.
- `{{title}}` — Issue or Pull Request title
- `{{author}}` — Issue or Pull Request author login
- `{{action}}` — Webhook action (e.g. `opened`, `synchronize`)
- `{{repository}}` — Repository full name (e.g. `octocat/Hello-World`)
- `{{event}}` — GitHub event type (`issues`, `pull_request`, `push`)
- `{{issueNumber}}` — Issue or Pull Request number
- `{{url}}` — HTML URL of the issue or PR on GitHub

### 5. Reliability & Best-Effort Delivery Semantics
- **At-least-once event processing with best-effort external action idempotency.**
- **No exactly-once guarantee**: Incoming Webhooks do not support distributed two-phase commit. A crash after Slack accepts the message but before `ActionExecution.Success` is saved in PostgreSQL can theoretically result in a duplicate notification on retry.
- **Action-level deduplication**: On event retry, `ActionDispatcher` skips actions whose `ActionExecution` status is already `SUCCESS`.
- **Partial Failure Handling**: If GitHub `AddLabel` succeeds and `SlackNotify` fails transiently (e.g. 429 or 5xx), only `SlackNotify` is retried on subsequent worker claims.

### 6. Local Testing Instructions
Mock test execution:
```bash
# Run all unit tests (including Slack error classification and templating tests)
dotnet test tests/GitHubBot.UnitTests/GitHubBot.UnitTests.csproj --nologo

# Run PostgreSQL integration tests
dotnet test tests/GitHubBot.IntegrationTests/GitHubBot.IntegrationTests.csproj --nologo
```
