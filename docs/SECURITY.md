# Security Controls

## Webhook HMAC Validation
All incoming webhooks are validated using the `X-Hub-Signature-256` header provided by GitHub.
- Validation uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.
- Each repository uses a unique, cryptographically secure 32-byte webhook secret generated locally.

## Secret Management & Encryption
- GitHub OAuth access tokens and per-repository webhook secrets are symmetrically encrypted using AES-256-GCM before being stored in the PostgreSQL database.
- The encryption key is sourced securely from the host environment variable (`Encryption__Key`).
- At no point are raw secrets logged, displayed to the frontend, or persisted unencrypted.

## OAuth Security & Authentication
- The system relies on GitHub OAuth for user identity.
- Cookies are issued using `HttpOnly`, `Secure`, and `SameSite=Lax` parameters for CSRF protection and script isolation.
- Vercel routes are protected to ensure no unauthorized access to dashboard data.

## Replay and Idempotency Protection
- GitHub Webhooks contain an `X-GitHub-Delivery` header.
- The system uses this ID as a unique constraint to ensure that duplicate webhook deliveries (retries initiated by GitHub) are safely ignored or idempotently processed.
- Action executions check for prior `SUCCESS` state, meaning a failed workflow will not spam a Slack channel with duplicate alerts if it partially succeeded previously.

## Repository Authorization
- Endpoints for listing, connecting, or configuring rules require the User ID attached to the repository connection to match the authenticated User ID.
- Cross-tenant data leakage is prevented via mandatory ownership filters in every database query.

## Logging Rules
The application strictly redacts or omits sensitive fields in logs:
- `Authorization` headers
- `EncryptedAccessToken` / `EncryptedWebhookSecret`
- GitHub App Private Keys
- Slack URLs
- Gemini API Keys
