# Live Demonstration Guide

## Deployed Environments
- **Frontend URL**: [https://git-hub-automation-bot.vercel.app](https://git-hub-automation-bot.vercel.app)
- **Backend API**: Hosted on Render
- **Database**: Hosted on Neon (PostgreSQL)

## 1. Login
1. Navigate to the Frontend URL.
2. Click **Login with GitHub**.
3. You will be authenticated securely via the application's OAuth flow. You should notice the premium branded loader on first launch!

## 2. Connect a Repository
1. Navigate to the **Repositories** tab.
2. Select an active repository from the dropdown and click **Connect**.
3. *Optional GitHub App Step*: If you have installed the "Event Automation Bot" GitHub App, the repository card will immediately detect it and display a "GitHub App installed" badge. If not, it falls back safely to OAuth.

## 3. Configure a Rule
1. Navigate to the **Rules** tab.
2. Click **Create New Rule**.
3. Define a condition (e.g., `TitleContains`, Value: "bug").
4. Define actions:
   - Add Label: `bug`
   - Post Comment: `Thanks for reporting this bug, we're looking into it.`
   - AI Triage (Gemini): `true`
5. Save the Rule.

## 4. Trigger Webhook Event
1. Go to your connected repository on GitHub.
2. Create a new Issue with the title: `Found a critical bug in the login page`.
3. Submit the issue.

## 5. Verify the Outcomes
Watch the magic happen within seconds!
- **GitHub Label**: Check the GitHub Issue; the `bug` label should be attached.
- **GitHub Comment**: The bot should have commented.
- **AI Triage**: If enabled, Gemini will have summarized the issue and the summary will appear in the bot's comment or your Slack channel (if configured).

## 6. Review Observability & Activity
1. Go back to the Bot application and navigate to the **Activity** tab.
2. You will see the event in real-time. 
3. Expand the event to view granular step-by-step metrics (e.g., Rule Evaluation duration, Add Label success status).

## 7. Demonstrate Retry/Failure Behavior
To prove the system's resilience:
1. Revoke the bot's repository permissions temporarily.
2. Fire a webhook event.
3. Observe the Activity tab as the event transitions from `PROCESSING` to `RETRYING` with exponential backoff timers displayed accurately.
