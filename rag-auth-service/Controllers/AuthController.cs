using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using RagAuthService.Models;

namespace RagAuthService.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly TokenService _tokenService;

    public AuthController(IConfiguration configuration, TokenService tokenService)
    {
        _configuration = configuration;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var connString = _configuration["MAIN_DB_URL"]!;
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT id, username, password_hash FROM users WHERE username = @u", conn);
        cmd.Parameters.AddWithValue("u", request.Username);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Unauthorized();

        var user = new User(reader.GetInt32(0), reader.GetString(1), reader.GetString(2));

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized();

        var access = _tokenService.GenerateAccessToken(user.Id.ToString(), user.Username);
        var refresh = _tokenService.GenerateRefreshToken(user.Id.ToString(), user.Username);
        return Ok(new TokenResponse(access, refresh, 900));
    }

    [HttpPost("refresh")]
    public IActionResult Refresh([FromBody] RefreshRequest request)
    {
        var principal = _tokenService.ValidateToken(request.RefreshToken);
        if (principal == null)
            return Unauthorized();

        var typ = principal.Claims.FirstOrDefault(c => c.Type == "typ")?.Value;
        if (typ != "refresh")
            return Unauthorized();

        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var username = principal.FindFirstValue(JwtRegisteredClaimNames.UniqueName) ?? principal.Identity?.Name;
        if (userId is null || username is null)
            return Unauthorized();

        var access = _tokenService.GenerateAccessToken(userId, username);
        var refresh = _tokenService.GenerateRefreshToken(userId, username);
        return Ok(new TokenResponse(access, refresh, 900));
    }

    [HttpPost("introspect")]
    public IActionResult Introspect([FromBody] IntrospectRequest request)
    {
        var principal = _tokenService.ValidateToken(request.Token, validateLifetime: false);
        if (principal == null)
            return Ok(new { active = false });

        var active = _tokenService.ValidateToken(request.Token) != null;

        return Ok(new
        {
            active,
            sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
            username = principal.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
        });
    }
}
