using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;
using RagBackgroundWorker;

IHost host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        var brokerUrl = configuration["MESSAGE_BROKER_URL"] ?? throw new InvalidOperationException("MESSAGE_BROKER_URL not configured");
        var cacheUrl = configuration["DATA_CACHE_URL"] ?? throw new InvalidOperationException("DATA_CACHE_URL not configured");
        var vectorDbUrl = configuration["VECTOR_DB_URL"] ?? throw new InvalidOperationException("VECTOR_DB_URL not configured");
        var mainDbUrl = configuration["MAIN_DB_URL"] ?? throw new InvalidOperationException("MAIN_DB_URL not configured");
        var aiHostUrl = configuration["AI_HOST_URL"] ?? throw new InvalidOperationException("AI_HOST_URL not configured");

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(cacheUrl));
        services.AddSingleton(_ => new NpgsqlConnection(mainDbUrl));
        services.AddHttpClient("vectorDb", c => { c.BaseAddress = new Uri(vectorDbUrl); });
        services.AddHttpClient("aiHost", c => { c.BaseAddress = new Uri(aiHostUrl); });

        services.AddMassTransit(x =>
        {
            x.AddConsumer<PromptConsumer>();
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(brokerUrl);
                cfg.ConfigureEndpoints(context);
            });
        });
    })
    .Build();

await host.RunAsync();
