using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using RagWebService.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace RagWebService.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/user-data")]
[Authorize]
[SwaggerTag("Manage per-user context used to augment prompts.")]
public class UserDataController : ControllerBase
{
    private readonly NpgsqlConnection _db;

    public UserDataController(NpgsqlConnection db)
    {
        _db = db;
    }

    [HttpGet]
    [SwaggerOperation(Summary = "Get current user's context", Description = "Returns the stored user-specific context data.")]
    public async Task<IActionResult> Get()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT data FROM user_data WHERE user_id = @uid LIMIT 1", conn);
        cmd.Parameters.AddWithValue("uid", userId.Value);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null)
            return NotFound();

        return Ok(new { data = result.ToString() ?? string.Empty });
    }

    [HttpPut]
    [SwaggerOperation(Summary = "Set current user's context", Description = "Creates or updates the stored user-specific context data.")]
    public async Task<IActionResult> Put([FromBody] UserDataRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        await using var conn = new NpgsqlConnection(_db.ConnectionString);
        await conn.OpenAsync();

        const string sql = @"INSERT INTO user_data (user_id, data)
                             VALUES (@uid, @data)
                             ON CONFLICT (user_id)
                             DO UPDATE SET data = EXCLUDED.data, updated_at = NOW()";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("uid", userId.Value);
        cmd.Parameters.AddWithValue("data", request.Data ?? string.Empty);
        await cmd.ExecuteNonQueryAsync();

        return NoContent();
    }

    private Guid? GetUserId()
    {
        var sub = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                  ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
