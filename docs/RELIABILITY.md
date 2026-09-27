# Reliability & State Machine

## Event Lifecycle
The webhook queue is designed to absorb massive traffic spikes without exhausting API rate limits or losing data.
1. **PENDING**: New webhook event is written directly to the database.
2. **PROCESSING**: Background worker claims the event, increments attempt count, and sets the `ClaimedAt` timestamp.
3. **SUCCESS**: Event rule evaluation and all action executions completed successfully.
4. **RETRYING**: Event encountered a downstream error (e.g., Gemini 503, Slack network failure) and is scheduled for a retry via exponential backoff.
5. **FAILED**: The maximum retry threshold was breached.

## Retry Policy & Exponential Backoff
A centralized `RetryCalculator` in the Domain layer handles exponential backoff with jitter:
- Formula: `Delay = BaseDelay * (2 ^ Attempt)` + `Random Jitter (0-15%)`
- The jitter prevents "thundering herd" problems where a recovering downstream API is immediately overwhelmed by synchronized retries.
- Maximum attempts: 6.

## Stale Event Recovery
If the background worker container crashes while an event is in the `PROCESSING` state, the event is technically locked.
- A recovery mechanism runs every cycle to identify events in `PROCESSING` whose `ClaimedAt` timestamp is older than 5 minutes.
- These events are forcibly reverted to `RETRYING` so another worker can pick them up.

## Downstream Outage Behavior
- **Gemini Outages**: If the primary AI model hits a 503 or quota limit, the system gracefully rolls over to a fallback model (`gemini-3.5-flash-lite`). If that fails, it initiates the retry lifecycle.
- **GitHub API Rate Limits**: Responses indicating rate limits will cause the event to pause and retry once the rate limit resets, preserving the webhook delivery.
- **Slack Failures**: Isolated to the specific Slack Action Execution, meaning a GitHub Comment success will not be rolled back just because Slack failed. The retry loop will uniquely target only the failed Slack action.
