using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using WorkflowType = Microsoft.Agents.AI.Workflows.Workflow;

namespace FinancialAI.Infrastructure.Workflow;

public static class WorkflowRegistration
{
    public static IServiceCollection AddReportWorkflow(this IServiceCollection services)
    {
        services.AddScoped<LoadProfileAndValidateExecutor>();
        services.AddScoped<CalculateProjectionsExecutor>();
        services.AddScoped<AnalysisExecutor>();
        services.AddScoped<ReviewExecutor>();
        services.AddScoped<HumanReviewExecutor>();
        services.AddScoped<PersistAndRenderExecutor>();

        services.AddSingleton<IReportWorkflowQueue, InMemoryReportWorkflowQueue>();
        services.AddScoped<IReportWorkflowRunner, MafReportWorkflowRunner>();

        services.AddScoped<WorkflowType>(sp =>
        {
            var load = sp.GetRequiredService<LoadProfileAndValidateExecutor>();
            var calc = sp.GetRequiredService<CalculateProjectionsExecutor>();
            var analysis = sp.GetRequiredService<AnalysisExecutor>();
            var review = sp.GetRequiredService<ReviewExecutor>();
            var humanReview = sp.GetRequiredService<HumanReviewExecutor>();
            var persist = sp.GetRequiredService<PersistAndRenderExecutor>();

            return new WorkflowBuilder(load)
                .AddEdge(load, calc)
                .AddEdge(calc, analysis)                 // was: .AddEdge(calc, retrieval)
                .AddEdge(analysis, review)               // was: .AddEdge(retrieval, analysis)
                                                         // edges from review unchanged — issue 6's fix
                .AddEdge<WorkflowState>(review, persist,
                    s => s.AiReview?.Decision == "approve")
                .AddEdge<WorkflowState>(review, analysis,
                    s => s.AiReview?.Decision == "revise")
                .AddEdge<WorkflowState>(review, humanReview,
                    s => s.AiReview?.Decision == "human_review")
                .WithOutputFrom(persist, humanReview)
                .Build();
        });

        services.AddHostedService<ReportWorkflowWorker>();

        return services;
    }
}