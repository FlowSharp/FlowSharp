namespace FlowSharp.Application.Abstractions;

/// <summary>
/// Workflow calistirmalarini sahip (owner) basina dakikalik kotaya gore sinirlandirir. Admin
/// sahipli workflow'lar ve sahipsiz (sistem) calismalar muaftir. Senkron (manuel/webhook) yolda
/// is olusturulmadan once kontrol edilir.
/// </summary>
public interface IWorkflowRunRateLimiter
{
    /// <summary>
    /// Verilen sahip icin limit dahilinde mi kontrol eder; dahilindeyse bir calistirma "harcar".
    /// Limit asilmissa <see cref="FlowSharp.Application.Workflows.WorkflowRateLimitedException"/> firlatir.
    /// </summary>
    Task EnsureWithinLimitAsync(string? ownerId, CancellationToken cancellationToken = default);
}
