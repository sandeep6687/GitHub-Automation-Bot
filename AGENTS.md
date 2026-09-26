# AGENTS.md — GitHub Automation Bot

> This file is the authoritative source for architectural decisions and implementation constraints.  
> Every developer and AI agent working on this codebase must follow these rules.

---

## Project Overview

An event-driven GitHub automation bot. Users authenticate via GitHub OAuth, connect repositories, define automation rules, and the bot reacts to webhook events by writing back to GitHub and notifying Slack. Deployed as a modular monolith on free-tier infrastructure.

## Technology Stack

| Layer | Technology |
|---|---|
| Backend | .NET 8, ASP.NET Core, C# 12 |
| ORM | Entity Framework Core 8 |
| Database | PostgreSQL (Neon free tier) |
| Frontend | React 18, Vite 5, TypeScript |
| Testing | xUnit, Moq, FluentAssertions |
| Hosting | Render (free tier) |
| Resilience | Polly |

---

## Architectural Constraints

### 1. Modular Monolith — Layer Rules

```
GitHubBot.Domain          → references: nothing
GitHubBot.Application     → references: Domain
GitHubBot.Infrastructure  → references: Domain, Application
GitHubBot.Api             → references: Application, Infrastructure
```

**Domain** contains: entities, enums, value objects, RuleEngine, RetryCalculator, repository interfaces.  
Domain has **zero NuGet dependencies**. No EF Core attributes, no System.Text.Json annotations, no HttpClient.

**Application** contains: services, DTOs, IActionHandler interface, ActionDispatcher, orchestration logic.  
Application depends on Domain only.

**Infrastructure** contains: EF Core DbContext, repository implementations, GitHub/Slack API clients, concrete action handlers (GitHubLabelActionHandler, SlackNotificationActionHandler, etc.), BackgroundService worker, encryption.  
Infrastructure implements interfaces defined in Domain (repositories) and Application (IActionHandler).

**API** contains: controllers (thin — no business logic), middleware, filters, Program.cs.

### 2. Layer Placement (non-negotiable)

| Component | Layer | Reason |
|---|---|---|
| RuleEngine | Domain | Pure logic: no I/O, no async, no side effects. Takes parsed data, returns results. |
| RetryCalculator | Domain | Pure calculation. No I/O. |
| IActionHandler | Application | Port interface. Defines what an action handler must do. |
| ActionDispatcher | Application | Orchestration. Routes to handlers by ActionType. |
| GitHubLabelActionHandler | Infrastructure | Makes HTTP calls to GitHub API. |
| GitHubCommentActionHandler | Infrastructure | Makes HTTP calls to GitHub API. |
| SlackNotificationActionHandler | Infrastructure | Makes HTTP calls to Slack API. |
| EventProcessingWorker | Infrastructure | BackgroundService. Uses IServiceScopeFactory. |

### 3. Reliability Semantics

- The system provides **at-least-once processing**, not exactly-once.
- DeliveryId uniqueness provides **webhook ingestion idempotency** — the same delivery is not persisted twice.
- DeliveryId does **NOT** guarantee exactly-once external side effects. GitHub/Slack calls may execute more than once across retries.
- **Action-level idempotency**: on retry, query existing `ActionExecution` records with `status = SUCCESS` for the event. Skip those actions. Only re-execute failed or un-attempted actions.
- A crash between an external call succeeding and persisting its Success record can cause a repeated call. This is an accepted trade-off.

### 4. Background Worker — Claim/Release Pattern

**NEVER hold PostgreSQL row locks while making external HTTP calls.**

The correct pattern:

```
1. BEGIN TRANSACTION
2. SELECT ... FOR UPDATE SKIP LOCKED (find candidates)
3. UPDATE status = 'PROCESSING', claimed_at = NOW(), attempt_count += 1
4. COMMIT  ← lock released here
5. Make external HTTP calls (no lock held)
6. UPDATE status = SUCCESS/RETRYING/FAILED
```

Stale claim recovery: events in `PROCESSING` with `claimed_at` older than 5 minutes are reset to `RETRYING`.

### 5. Webhook Endpoint Rules

- Read raw request body BEFORE any JSON parsing (required for HMAC verification).
- Verify HMAC-SHA256 signature using `CryptographicOperations.FixedTimeEquals` (timing-safe).
- Use per-repository webhook secrets.
- Persist the event, then return `202 Accepted`.
- **Do NOT** execute GitHub or Slack actions in the webhook handler. Processing is always asynchronous via the background worker.

### 6. Rule System

Keep it simple. The three condition types are:

- `TitleContains` — checks issue/PR title
- `AuthorEquals` — checks issue/PR author login
- `LabelContains` — checks if any label name contains the value

All conditions within a rule are evaluated with AND logic.

**Do NOT** create a generic expression language, dynamic field path navigation, or configurable JSON path extraction. Each condition type knows exactly which JSON paths to check.

### 7. AI Integration

AI (Gemini triage) is a **stretch goal**, not in the MVP.

- Do NOT add AI-specific database fields (ai_summary, ai_suggested_label, ai_priority) to the initial migration.
- Do NOT include AI in the core event processing path.
- When implemented later, AI will be an opt-in post-processing step with its own migration.

### 8. Excluded Technologies

Do NOT introduce any of the following:

- Kafka
- Redis
- Kubernetes
- Microservices architecture
- GraphQL
- Generic expression languages
- Dynamic scripting engines

---

## Implementation Rules

### Controllers

- Controllers must be thin. No business logic.
- Controllers call Application services, never Infrastructure directly.
- Every mutation endpoint must verify resource ownership.

### Services

- Application services orchestrate domain logic and infrastructure calls.
- Services receive domain interfaces via constructor injection.
- Services must use `CancellationToken` on all async methods.

### Database

- Use EF Core Fluent API for all configurations. No data annotations on entities.
- Domain entities must not reference EF Core types.
- Use `JSONB` for raw webhook payloads and action configurations.
- Use `TIMESTAMPTZ` for all timestamps. Store as UTC.

### Security

- GitHub access tokens encrypted at rest (AES-256-GCM).
- Webhook secrets are per-repository, generated with `RandomNumberGenerator`.
- Never log: access tokens, client secrets, webhook secrets, Slack webhook URLs.
- JWT in HTTP-only, Secure, SameSite=Strict cookie.
- Never expose tokens to the frontend.

### Error Handling

- Use global exception handling middleware.
- Never expose stack traces or internal error details to clients.
- Log errors with structured fields (EventId, DeliveryId, CorrelationId).
- Failed actions must be recorded in ActionExecution with error details.

---

## Testing Priorities

### Must Have (unit tests, no mocks needed)

1. RuleEngine — all condition types, AND logic, disabled rules, edge cases
2. RetryCalculator — delay ranges, exhaustion, boundary conditions
3. WebhookSignatureValidator — valid, invalid, missing prefix, empty

### Must Have (unit tests with mocks)

4. ActionDispatcher — routing, unknown type handling
5. EventProcessingService — action-level idempotency (skip succeeded)
6. WebhookIngestionService — duplicate delivery handling

### Must Have (integration tests)

7. Webhook endpoint — valid POST, invalid signature, duplicate delivery
8. Worker — claim/release, stale recovery

---

## File Naming Conventions

- Entities: `PascalCase.cs` (e.g., `WebhookEvent.cs`)
- Interfaces: `I` prefix (e.g., `IActionHandler.cs`)
- Tests: `{ClassUnderTest}Tests.cs`
- Configurations: `{Entity}Configuration.cs`
- Controllers: `{Resource}Controller.cs`

---

## Commit Message Convention

```
type(scope): description

feat(webhook): add HMAC signature verification
fix(worker): prevent row lock during HTTP calls
test(rules): add TitleContains condition tests
refactor(domain): move RuleEngine from Application to Domain
docs(agents): update architectural constraints
```

Types: feat, fix, test, refactor, docs, chore
