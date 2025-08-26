using MassTransit;
using Npgsql;
using StackExchange.Redis;

namespace RagBackgroundWorker;

public class PromptConsumer : IConsumer<PromptMessage>
{
    private readonly IConnectionMultiplexer _cache;
    private readonly NpgsqlConnection _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public PromptConsumer(IConnectionMultiplexer cache, NpgsqlConnection db, IHttpClientFactory httpClientFactory)
    {
        _cache = cache;
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    public async Task Consume(ConsumeContext<PromptMessage> context)
    {
        var taskId = context.Message.TaskId;
        var prompt = context.Message.Prompt;

        Console.WriteLine($"Processing task {taskId} with prompt '{prompt}'");

        // TODO: Check Redis/Qdrant/PostgreSQL, forward to AI host, and store responses
        await Task.CompletedTask;
    }
}
