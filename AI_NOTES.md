# AI Implementation Notes

This document tracks engineering decisions, architectural trade-offs, and progress across vertical implementation slices.

---

## Phase 0: Foundation

### Actions Completed
1. **Solution Structure**: Created `GitHubAutomationBot.sln` with 4 source projects (`GitHubBot.Domain`, `GitHubBot.Application`, `GitHubBot.Infrastructure`, `GitHubBot.Api`) and 2 test projects (`GitHubBot.UnitTests`, `GitHubBot.IntegrationTests`).
2. **Project References**:
   - `Domain`: 0 references, 0 NuGet packages.
   - `Application`: References `Domain` only.
   - `Infrastructure`: References `Domain` and `Application`.
   - `Api`: References `Application` and `Infrastructure`.
   - `UnitTests`: References `Domain`, `Application`, `Infrastructure`, with `Moq` and `FluentAssertions`.
   - `IntegrationTests`: References all layers including `Api`.
3. **Architectural Guardrails (`ArchitectureTests.cs`)**:
   - Enforces Domain has zero NuGet dependencies (no EF Core, no Npgsql, no Polly, no Newtonsoft).
   - Enforces Application does not reference Infrastructure.
   - Enforces Application references Domain.
   - Enforces Infrastructure references Domain and Application.
4. **Target Framework & Runtime Roll-Forward**:
   - All projects target `net8.0` in alignment with production specification (`AGENTS.md`).
   - Added `<RollForward>Major</RollForward>` to executable and test projects so local environments running .NET 9 SDK/runtime can execute tests and builds seamlessly without breaking the .NET 8 target.
5. **Configuration Skeleton**:
   - Added `appsettings.json` and `appsettings.Development.json` in `GitHubBot.Api` with sections for ConnectionStrings, GitHub OAuth, Slack OAuth, JWT, AES Encryption, and Worker parameters.
   - Created `.env.example` mapping all environment variables.
6. **Containerization**:
   - Multi-stage `Dockerfile` with test execution before final publish.
   - `docker-compose.yml` defining PostgreSQL 16 container and API service with health checks.
7. **Verification**:
   - `dotnet build` succeeds with 0 errors, 0 warnings.
   - `dotnet test` executes and passes all tests (including architectural boundary tests).

---

## Next Up: Phase 1 (Database + Domain)
- Create Domain entities (`User`, `GithubAccount`, `ConnectedRepository`, `Rule`, `RuleCondition`, `RuleAction`, `WebhookEvent`, `ActionExecution`).
- Note requirement from review: `WebhookEvent.RepositoryId` is `NOT NULL`.
- Define repository interfaces in Domain.
- EF Core DbContext + Fluent API configurations in Infrastructure.
- Initial migration.
