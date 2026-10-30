using FinancialAI.Application.Projections;
using FinancialAI.Application.Workflow;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialAI.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IProjectionCalculator, ProjectionCalculator>();

        services.AddOptions<ReportWorkflowOptions>()
            .BindConfiguration(ReportWorkflowOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
