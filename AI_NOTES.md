# AI Collaboration Notes

## 1. AI Tools/Models Used
This project was developed through pair-programming between a human engineer and **Antigravity**, an agentic AI coding assistant powered by Gemini.

## 2. Division of Labor
**Human Responsibilities:**
- Architectural constraints (Modular Monolith, Clean Architecture).
- Security definitions (Encryption, HMAC).
- Defining the boundaries for reliability (PostgreSQL locking logic, Retry models).
- Identifying scope and requirements.

**AI Responsibilities:**
- Rapidly implementing backend boilerplate (Controllers, DTOs, EF Core mappings).
- Translating architectural constraints into functional C# code.
- Building out the React frontend components, including robust UX paradigms like the dual-loading system.
- Diagnosing and fixing compilation and integration bugs during iterative development.
- Writing extensive unit and architecture tests.

## 3. Important Human Architectural Decisions
- **DB-backed queue instead of Kafka**: Kafka is overkill for a lightweight GitHub bot. Using PostgreSQL `SKIP LOCKED` allowed us to build a highly concurrent event-processing background worker on a free-tier database.
- **Repository-specific webhook secrets**: Instead of one global secret, every repository gets a uniquely generated, encrypted secret, significantly reducing the blast radius of a compromised token.
- **Idempotent action execution**: The system tracks action executions per event, allowing it to skip previously successful actions (like sending a Slack message) if an event is retried due to a subsequent failure.

## 4. Hardest AI-Assisted Mistake
**The GitHub App Constructor Mismatch**
During Phase 14, the AI was tasked with implementing GitHub App authentication detection. The AI successfully updated the `RepositoryService` and `GitHubAppTokenProvider` classes to accept new dependencies (e.g., `IGitHubAppTokenProvider` and `ILogger`).

*The Mistake*: The AI updated the production API injection perfectly but forgot to update the mock constructions in the Unit Test suite (`RepositoryServiceTests` and `GitHubAppTokenProviderTests`).
*How it was caught*: The application compiled and ran locally, but the Docker build on Render failed explicitly during `dotnet test` because of the signature mismatch (`CS1503`).
*How it was fixed*: The AI was prompted with the Render failure logs. It inspected the actual production constructors, updated the test constructors to match, and injected the required `Mock<T>` objects, ultimately passing the suite.
*Lesson Learned*: When AI modifies core class signatures, explicitly prompting it to update the test suite simultaneously prevents broken CI pipelines.

## 5. What would be improved with more time
- **Abstracting the Database Provider**: We are tightly coupled to PostgreSQL currently. More time would allow abstracting EF Core configurations to seamlessly swap between SQLite (for local devs) and Postgres.
- **Enhanced AI Analytics**: Currently, AI is used solely for triage text generation. With more time, we'd implement vector embeddings to allow the bot to identify duplicate issues based on semantic similarity to past issues.
- A future extension would enable AI-assisted pull-request review, with automated approval gated by explicit repository policies and deterministic checks.
