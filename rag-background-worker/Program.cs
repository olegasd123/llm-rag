using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StackExchange.Redis;
using RagBackgroundWorker;
using System.Net.Http.Headers;

IHost host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        var brokerUrl = configuration["MESSAGE_BROKER_URL"] ?? throw new InvalidOperationException("MESSAGE_BROKER_URL not configured");
        var cacheUrl = configuration["DATA_CACHE_URL"] ?? throw new InvalidOperationException("DATA_CACHE_URL not configured");
        var vectorDbUrl = configuration["VECTOR_DB_URL"] ?? throw new InvalidOperationException("VECTOR_DB_URL not configured");
        var mainDbConnectionString = configuration["MAIN_DB_CONNECTION_STRING"] ?? throw new InvalidOperationException("MAIN_DB_CONNECTION_STRING not configured");
        var aiHostUrl = configuration["AI_HOST_URL"] ?? throw new InvalidOperationException("AI_HOST_URL not configured");
        var aiHostApiKey = configuration["AI_HOST_API_KEY"]; // optional

        var redisOptions = ConfigurationOptions.Parse(cacheUrl);
        redisOptions.AbortOnConnectFail = false; // keep retrying until Redis is ready
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
        services.AddSingleton(_ => new NpgsqlConnection(mainDbConnectionString));
        services.AddHttpClient("vectorDb", c => { c.BaseAddress = new Uri(vectorDbUrl); });
        services.AddHttpClient("aiHost", c =>
        {
            c.BaseAddress = new Uri(aiHostUrl);
            if (!string.IsNullOrWhiteSpace(aiHostApiKey))
            {
                c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiHostApiKey);
            }
        });

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
