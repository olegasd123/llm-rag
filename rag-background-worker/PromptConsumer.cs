using MassTransit;
using Npgsql;
using StackExchange.Redis;
using System.Net.Http.Json;

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
        var cacheDb = _cache.GetDatabase();

        // 1. Check if we already have a cached response for this task
        var responseKey = $"response:{taskId}";
        var cachedResponse = await cacheDb.StringGetAsync(responseKey);
        if (!cachedResponse.IsNullOrEmpty)
        {
            Console.WriteLine($"Cached response found for task {taskId}");
            return;
        }

        // 2. Retrieve vector context, using Redis cache when possible
        var vectorKey = $"vector:{prompt}";
        string vectorContext;
        var cachedVector = await cacheDb.StringGetAsync(vectorKey);
        if (!cachedVector.IsNullOrEmpty)
        {
            vectorContext = cachedVector!
                .ToString();
        }
        else
        {
            var vectorClient = _httpClientFactory.CreateClient("vectorDb");
            try
            {
                // Placeholder endpoint; if unavailable, continue without vector context
                var vectorResp = await vectorClient.GetAsync($"/search?text={Uri.EscapeDataString(prompt)}");
                if (vectorResp.IsSuccessStatusCode)
                {
                    vectorContext = await vectorResp.Content.ReadAsStringAsync();
                    await cacheDb.StringSetAsync(vectorKey, vectorContext);
                }
                else
                {
                    Console.WriteLine($"Vector DB returned {vectorResp.StatusCode}; proceeding without vector context.");
                    vectorContext = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Vector DB request failed: {ex.Message}");
                vectorContext = string.Empty;
            }
        }

        // 3. Retrieve additional user data from PostgreSQL (cached in Redis)
        var userKey = $"user:{taskId}";
        string userContext;
        var cachedUser = await cacheDb.StringGetAsync(userKey);
        if (!cachedUser.IsNullOrEmpty)
        {
            userContext = cachedUser!;
        }
        else
        {
            await using var userConn = new NpgsqlConnection(_db.ConnectionString);
            await userConn.OpenAsync();
            using var cmd = new NpgsqlCommand("SELECT data FROM user_data LIMIT 1", userConn);
            var result = await cmd.ExecuteScalarAsync();
            userContext = result?.ToString() ?? string.Empty;
            await cacheDb.StringSetAsync(userKey, userContext);
        }

        // 4. Forward augmented prompt to the AI host
        var augmentedPrompt = $"{prompt}\n{vectorContext}\n{userContext}";
        var aiClient = _httpClientFactory.CreateClient("aiHost");
        string generated;
        try
        {
            var aiResp = await aiClient.PostAsJsonAsync("/generate", new { prompt = augmentedPrompt });
            if (aiResp.IsSuccessStatusCode)
            {
                generated = await aiResp.Content.ReadAsStringAsync();
            }
            else
            {
                Console.WriteLine($"AI host returned {aiResp.StatusCode}; using fallback response.");
                generated = $"Echo: {prompt}";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AI host request failed: {ex.Message}; using fallback response.");
            generated = $"Echo: {prompt}";
        }

        // 5. Store response in PostgreSQL and cache
        await using (var conn = new NpgsqlConnection(_db.ConnectionString))
        {
            await conn.OpenAsync();
            using var insert = new NpgsqlCommand("INSERT INTO responses(task_id, prompt, response) VALUES (@taskId, @prompt, @response)", conn);
            insert.Parameters.AddWithValue("taskId", taskId);
            insert.Parameters.AddWithValue("prompt", prompt);
            insert.Parameters.AddWithValue("response", generated);
            await insert.ExecuteNonQueryAsync();
        }

        await cacheDb.StringSetAsync(responseKey, generated);
    }
}
