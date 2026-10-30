using MassTransit;

namespace FinancialAI.Infrastructure.Messaging;

public record ReportRequested(Guid ReportId);
public record ReviewRequired(Guid ReportId);
public record ReportCompleted(Guid ReportId);

public sealed class ReportSagaState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = null!;
    public string ReportId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class ReportSaga : MassTransitStateMachine<ReportSagaState>
{
    public State Running { get; private set; } = null!;
    public State AwaitingReview { get; private set; } = null!;
    public State Completed { get; private set; } = null!;

    public Event<ReportRequested> ReportRequested { get; private set; } = null!;
    public Event<ReportCompleted> ReportCompleted { get; private set; } = null!;
    public Event<ReviewRequired> ReviewRequired { get; private set; } = null!;

    public ReportSaga()
    {
        InstanceState(x => x.CurrentState);

        Event(() => ReportRequested, e => e.CorrelateById(ctx => ctx.Message.ReportId));
        Event(() => ReportCompleted, e => e.CorrelateById(ctx => ctx.Message.ReportId));
        Event(() => ReviewRequired, e => e.CorrelateById(ctx => ctx.Message.ReportId));

        Initially(
            When(ReportRequested)
                .Then(ctx =>
                {
                    ctx.Saga.ReportId = ctx.Message.ReportId.ToString();
                    ctx.Saga.Status = "running";
                    ctx.Saga.CreatedAtUtc = DateTimeOffset.UtcNow;
                })
                .TransitionTo(Running));

        During(Running,
            When(ReviewRequired)
                .Then(ctx => ctx.Saga.Status = "needs_review")
                .TransitionTo(AwaitingReview),
            When(ReportCompleted)
                .Then(ctx =>
                {
                    ctx.Saga.Status = "completed";
                    ctx.Saga.CompletedAtUtc = DateTimeOffset.UtcNow;
                })
                .TransitionTo(Completed)
                .Finalize());

        SetCompletedWhenFinalized();
    }
}
