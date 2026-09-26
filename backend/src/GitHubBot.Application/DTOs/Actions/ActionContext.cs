using GitHubBot.Domain.Entities;

namespace GitHubBot.Application.DTOs.Actions;

public class ActionContext
{
    public WebhookEvent WebhookEvent { get; }
    public RuleAction RuleAction { get; }
    public ConnectedRepository Repository { get; }
    public int? IssueOrPrNumber { get; }
    public int AttemptNumber { get; }

    public ActionContext(
        WebhookEvent webhookEvent,
        RuleAction ruleAction,
        ConnectedRepository repository,
        int? issueOrPrNumber,
        int attemptNumber)
    {
        WebhookEvent = webhookEvent ?? throw new ArgumentNullException(nameof(webhookEvent));
        RuleAction = ruleAction ?? throw new ArgumentNullException(nameof(ruleAction));
        Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        IssueOrPrNumber = issueOrPrNumber;
        AttemptNumber = attemptNumber;
    }
}
