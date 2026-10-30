using Polly.Timeout;
using Polly.Retry;
using Polly;
using Azure.Messaging.ServiceBus;
using FinancialAI.Application.Ports;
using FinancialAI.Infrastructure.AiService;
using FinancialAI.Infrastructure.Cosmos;
using FinancialAI.Infrastructure.Messaging;
using FinancialAI.Infrastructure.Pdf;
using MassTransit;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Azure.Messaging.ServiceBus.Administration;


namespace FinancialAI.Infrastructure;

public static class DependencyInjection
{

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            var cs = configuration["Cosmos:ConnectionString"]
                ?? configuration["Cosmos__ConnectionString"]
                ?? throw new InvalidOperationException("Cosmos:ConnectionString is required.");

            var options = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                },
                ConnectionMode = ConnectionMode.Gateway,
            };

            // Development-only: emulator serves a self-signed cert on TLS 1.2 only.
            if (configuration.GetValue<bool>("Cosmos:UseEmulator"))
            {
                options.HttpClientFactory = () => new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                });
            }

            return new CosmosClient(cs, options);
        });

        // Bootstrap the database and containers in Development (and only in
        // Development). In production, containers are provisioned by Bicep and
        // the API runs under an identity with data-plane-only permissions —
        // CreateDatabaseIfNotExistsAsync would return 403 Forbidden.
        if (configuration.GetValue<bool>("Cosmos:BootstrapContainers", defaultValue: false))
        {
            services.AddHostedService<CosmosBootstrapperService>();
        }

        services.AddScoped<IFinancialProfileRepository, CosmosFinancialProfileRepository>();
        services.AddScoped<IReportRepository, CosmosReportRepository>();
        services.AddScoped<IOutboxRepository, CosmosOutboxRepository>();

        services.AddSingleton<IReportRenderer, QuestPdfReportRenderer>();
        services.AddScoped<IProcessedMessageStore, CosmosProcessedMessageStore>();
        services.AddScoped<IHumanReviewTaskRepository, CosmosHumanReviewTaskRepository>();

        services.AddHostedService<DevProfileSeeder>();

        services.AddHttpClient<IAiServiceClient, AiServiceClient>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["AiService:BaseUrl"]
                ?? configuration["AiService__BaseUrl"]
                ?? throw new InvalidOperationException("AiService:BaseUrl is required."));
            client.Timeout = TimeSpan.FromSeconds(
                configuration.GetValue("AiService:TimeoutSeconds", 120));
        })
        .AddResilienceHandler("ai-pipeline", (builder, context) =>
        {
            builder.AddRetry(new Polly.Retry.RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = Polly.DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Result is null
                    || (int)(args.Outcome.Result?.StatusCode ?? 0) >= 500),
            });
            builder.AddTimeout(TimeSpan.FromSeconds(90));
        });

        var serviceBusEnabled = configuration.GetValue<bool>("ServiceBus:Enabled", defaultValue: false);

        if (serviceBusEnabled)
        {
            var serviceBusConnectionString =
                configuration["ServiceBus__ConnectionString"]
                ?? configuration["ServiceBus:ConnectionString"]
                ?? throw new InvalidOperationException(
                    "ServiceBus connection string is not configured. " +
                    "Set ServiceBus__ConnectionString or ServiceBus:ConnectionString.");

            services.AddHostedService<ServiceBusOutboxDispatcher>();

            services.AddMassTransit(cfg =>
            {
                cfg.AddSagaStateMachine<ReportSaga, ReportSagaState>()
                    .CosmosRepository(r =>
                    {
                        if (configuration.GetValue<bool>("ServiceBus:UseEmulator"))
                        {
                            r.ConfigureEmulator();
                        }
                        r.DatabaseId = "financialai";
                        r.CollectionId = "sagas";
                    });

                // Register every IConsumer<> in this assembly. ReportCompletedConsumer
                // was never registered — that is why, even with a correctly-enveloped
                // message, the consumer had no endpoint to receive it.
                cfg.AddConsumers(typeof(ServiceBusOutboxDispatcher).Assembly);

                // Local development: in-memory transport.
                // Production: Azure Service Bus.
                //
                // The Service Bus emulator caps DefaultMessageTimeToLive at 1 hour;
                // MassTransit defaults every entity (queue, topic, subscription) to
                // 366 days, and v8 has no clean per-entity override for the
                // subscription a receive endpoint auto-creates. That is a structural
                // mismatch, not a configuration bug — v9 supports the emulator
                // natively but requires a commercial license.
                //
                // The in-memory transport is a drop-in at the application layer:
                // IPublishEndpoint, IBus, saga persistence, and consumers all behave
                // identically. The only difference is that messages do not leave the
                // process, so the emulator's TTL restrictions never apply.
                if (configuration.GetValue<bool>("ServiceBus:UseEmulator"))
                {
                    cfg.UsingInMemory((ctx, bus) =>
                    {
                        bus.ConfigureEndpoints(ctx);
                    });
                }
                else
                {
                    var serviceBusConnectionString =
                        configuration["ServiceBus__ConnectionString"]
                        ?? configuration["ServiceBus:ConnectionString"]
                        ?? throw new InvalidOperationException(
                            "ServiceBus connection string is not configured. " +
                            "Set ServiceBus__ConnectionString or ServiceBus:ConnectionString.");

                    var sbClient = new ServiceBusClient(serviceBusConnectionString);
                    var adminClient = new ServiceBusAdministrationClient(serviceBusConnectionString);

                    var endpointString = serviceBusConnectionString
                        .Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .First(p => p.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
                        .Substring("Endpoint=".Length);

                    cfg.UsingAzureServiceBus((ctx, bus) =>
                    {
                        bus.Host(new Uri(endpointString), sbClient, adminClient);
                        bus.ConfigureEndpoints(ctx);
                    });
                }
            });
        }

        return services;
    }
}










