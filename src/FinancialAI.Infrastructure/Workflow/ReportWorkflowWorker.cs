using FinancialAI.Application.Ports;
using FinancialAI.Application.Workflow;
using FinancialAI.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinancialAI.Infrastructure.Workflow;

public sealed class ReportWorkflowWorker : BackgroundService
{
    private readonly IReportWorkflowQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportWorkflowWorker> _logger;

    public ReportWorkflowWorker(
        IReportWorkflowQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ReportWorkflowWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var state in _queue.ReadAllAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IReportWorkflowRunner>();
            var reports = scope.ServiceProvider.GetRequiredService<IReportRepository>();

            using var logScope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["ReportId"] = state.ReportId.ToString("N"),
                ["CorrelationId"] = state.CorrelationId,
            });

            try
            {
                _logger.LogInformation("Running report workflow.");
                await runner.RunAsync(state, stoppingToken);
                _logger.LogInformation("Report workflow completed.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Report workflow cancelled during shutdown.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Report workflow failed; marking report as failed.");
                try
                {
                    var report = await reports.GetAsync(
                        state.ReportId.ToString("N"), stoppingToken);
                    if (report is not null)
                    {
                        await reports.SaveAsync(
                            report with { Status = ReportStatus.Failed.ToWireString() },
                            stoppingToken);
                    }
                }
                catch (Exception markEx)
                {
                    _logger.LogError(markEx, "Failed to mark report as failed.");
                }
            }
        }
    }
}