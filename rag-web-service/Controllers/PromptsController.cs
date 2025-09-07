using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RagWebService.Models;
using Swashbuckle.AspNetCore.Annotations;
using StackExchange.Redis;
using Npgsql;

namespace RagWebService.Controllers;

[ApiController]
[Route("prompts")]
[SwaggerTag("Handles submission of prompts and retrieval of generated results.")]
public class PromptsController : ControllerBase
{
    private readonly IPublishEndpoint _publisher;
    private readonly IConnectionMultiplexer _cache;
    private readonly NpgsqlConnection _db;

    public PromptsController(IPublishEndpoint publisher, IConnectionMultiplexer cache, NpgsqlConnection db)
    {
        _publisher = publisher;
        _cache = cache;
        _db = db;
    }

    [HttpPost]
    [Authorize]
    [SwaggerOperation(Summary = "Submit a prompt", Description = "Queues a prompt for processing and returns a task identifier.")]
    public async Task<IActionResult> SubmitPrompt([FromBody] PromptRequest request)
    {
        var taskId = Guid.NewGuid();
        // Extract user id from JWT (sub claim)
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            return Unauthorized(new { error = "Invalid or missing user id in token" });
        }

        await _publisher.Publish(new RagBackgroundWorker.PromptMessage(taskId, userId, request.Prompt));
        return Accepted(new { taskId });
    }

    [HttpGet("{id}")]
    [Authorize]
    [SwaggerOperation(Summary = "Get prompt result", Description = "Retrieves the generated result for the specified task id.")]
    public async Task<IActionResult> GetResult(string id)
    {
        var cacheDb = _cache.GetDatabase();
        var cacheKey = $"response:{id}";
        var cached = await cacheDb.StringGetAsync(cacheKey);
        if (!cached.IsNullOrEmpty)
        {
            return Ok(new { response = cached.ToString() });
        }

        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        using var cmd = new NpgsqlCommand("SELECT response FROM responses WHERE task_id = @id", conn);
        if (!Guid.TryParse(id, out var taskGuid))
        {
            return BadRequest(new { error = "Invalid task id format" });
        }
        cmd.Parameters.AddWithValue("id", taskGuid);
        var result = await cmd.ExecuteScalarAsync();
        if (result == null)
        {
            return NotFound();
        }

        var response = result.ToString() ?? string.Empty;
        await cacheDb.StringSetAsync(cacheKey, response);
        return Ok(new { response });
    }
}
