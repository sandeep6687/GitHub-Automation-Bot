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
- [ ] **Phase 1**: Database & Domain (Entities, EF Core DbContext, migrations, repository interfaces)
- [ ] **Phase 2**: GitHub OAuth (Authentication flow, JWT cookie issuance)
- [ ] **Phase 3**: Repository Connection (Repo listing, webhook secret generation & registration)
- [ ] **Phase 4**: Webhook Ingestion (HMAC validation, deduplication, 202 Accepted)
- [ ] **Phase 5**: Background Worker (Claim/release pattern, resilient processing)
- [ ] **Phase 6**: Rule Engine (TitleContains, AuthorEquals, LabelContains)
- [ ] **Phase 7**: GitHub Actions (Labeling, commenting, action-level idempotency)
- [ ] **Phase 8**: Slack Notifications
- [ ] **Phase 9**: Activity Log & Dashboard API
- [ ] **Phase 10**: Frontend Dashboard (React + Vite)
- [ ] **Phase 11**: End-to-End Verification & Deployment
