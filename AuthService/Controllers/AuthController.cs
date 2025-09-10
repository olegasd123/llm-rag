using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using RagAuthService.Models;
using Swashbuckle.AspNetCore.Annotations;

namespace RagAuthService.Controllers;

[ApiController]
[Route("auth")]
[SwaggerTag("Provides authentication endpoints including login, token refresh, and token introspection.")]
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
    [SwaggerOperation(Summary = "Authenticate user", Description = "Verifies credentials and returns both access and refresh tokens.")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        Console.WriteLine($"Login attempt for {request.Email}");

        var connString = _configuration["MAIN_DB_CONNECTION_STRING"]!;
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT id, email, password_hash FROM users WHERE email = @e", conn);
        cmd.Parameters.AddWithValue("e", request.Email);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Unauthorized();

        var user = new User(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized();

        // Generate tokens
        var access = _tokenService.GenerateAccessToken(user.Id.ToString(), user.Email);
        var refresh = _tokenService.GenerateRefreshToken(user.Id.ToString(), user.Email);

        // Ensure we can insert after using the reader
        await reader.DisposeAsync();

        const string upsertSql = @"
            INSERT INTO refresh_tokens (user_id, token, expires_at)
            VALUES (@uid, @token, @exp)
            ON CONFLICT (user_id)
            DO UPDATE SET token = EXCLUDED.token, expires_at = EXCLUDED.expires_at, rotated_at = now();";
        await using (var upsertCmd = new NpgsqlCommand(upsertSql, conn))
        {
            upsertCmd.Parameters.AddWithValue("uid", user.Id);
            upsertCmd.Parameters.AddWithValue("token", TokenHashing.ComputeHash(refresh));
            upsertCmd.Parameters.AddWithValue("exp", DateTime.UtcNow.AddSeconds(_tokenService.RefreshTokenExpiryInSeconds));
            await upsertCmd.ExecuteNonQueryAsync();
        }

        // Return both access and refresh tokens to the client
        return Ok(new TokenResponse(access, _tokenService.AccessTokenExpiryInSeconds, refresh, _tokenService.RefreshTokenExpiryInSeconds));
    }

    [HttpPost("refresh")]
    [SwaggerOperation(
        Summary = "Refresh tokens",
        Description = "Client sends refresh token. If it matches the server-stored token, rotate and return new access and refresh tokens. On mismatch, revoke and require re-login.")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        Console.WriteLine($"Token refresh attempt (client-sent refresh token)");

        // Validate the provided refresh token fully
        var refreshPrincipal = _tokenService.ValidateToken(request.RefreshToken, validateLifetime: true);
        if (refreshPrincipal == null)
            return Unauthorized();

        var typ = refreshPrincipal.Claims.FirstOrDefault(c => c.Type == "typ")?.Value;
        if (typ != "refresh")
            return Unauthorized();

        var userIdStr = refreshPrincipal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var email = refreshPrincipal.FindFirstValue(JwtRegisteredClaimNames.Email);
        if (userIdStr is null || email is null)
            return Unauthorized();

        var connString = _configuration["MAIN_DB_CONNECTION_STRING"]!;
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // Load stored refresh token
        const string selectSql = "SELECT token, expires_at FROM refresh_tokens WHERE user_id = @uid";
        await using var selectCmd = new NpgsqlCommand(selectSql, conn);
        selectCmd.Parameters.AddWithValue("uid", Guid.Parse(userIdStr));
        await using var reader = await selectCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Unauthorized();

        var storedRefreshHash = reader.GetString(0);
        var expiresAt = reader.GetDateTime(1);

        // If DB does not contain the same token (reuse/mismatch), revoke the session
        var providedHash = TokenHashing.ComputeHash(request.RefreshToken);
        if (!string.Equals(storedRefreshHash, providedHash, StringComparison.Ordinal))
        {
            await reader.DisposeAsync();
            const string revokeSql = "DELETE FROM refresh_tokens WHERE user_id = @uid";
            await using var revokeCmd = new NpgsqlCommand(revokeSql, conn);
            revokeCmd.Parameters.AddWithValue("uid", Guid.Parse(userIdStr));
            await revokeCmd.ExecuteNonQueryAsync();
            return Unauthorized();
        }

        if (expiresAt <= DateTime.UtcNow)
            return Unauthorized();

        // Rotate refresh token and issue new access token
        var newAccess = _tokenService.GenerateAccessToken(userIdStr, email);
        var newRefresh = _tokenService.GenerateRefreshToken(userIdStr, email);

        await reader.DisposeAsync();

        const string updateSql = @"
            UPDATE refresh_tokens
            SET token = @token, expires_at = @exp, rotated_at = now()
            WHERE user_id = @uid";
        await using var updateCmd = new NpgsqlCommand(updateSql, conn);
        updateCmd.Parameters.AddWithValue("uid", Guid.Parse(userIdStr));
        updateCmd.Parameters.AddWithValue("token", TokenHashing.ComputeHash(newRefresh));
        updateCmd.Parameters.AddWithValue("exp", DateTime.UtcNow.AddSeconds(_tokenService.RefreshTokenExpiryInSeconds));
        await updateCmd.ExecuteNonQueryAsync();

        // Return new access and refresh tokens
        return Ok(new TokenResponse(newAccess, _tokenService.AccessTokenExpiryInSeconds, newRefresh, _tokenService.RefreshTokenExpiryInSeconds));
    }

    [HttpPost("introspect")]
    [SwaggerOperation(Summary = "Introspect token", Description = "Validates a token and returns its activity and claims.")]
    public IActionResult Introspect([FromBody] IntrospectRequest request)
    {
        Console.WriteLine($"Token introspection attempt");

        var principal = _tokenService.ValidateToken(request.Token, validateLifetime: false);
        if (principal == null)
            return Ok(new { active = false });

        var active = _tokenService.ValidateToken(request.Token) != null;

        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
        return Ok(new { active, sub, email });
    }

    [HttpPost("logout")]
    [SwaggerOperation(Summary = "Logout user", Description = "Revokes the server-side refresh token for the user. Accepts an access token (can be expired).")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        Console.WriteLine("Logout attempt");

        var principal = _tokenService.ValidateToken(request.Token, validateLifetime: false);
        if (principal == null)
            return Unauthorized();

        var userIdStr = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(userIdStr))
            return Unauthorized();

        var connString = _configuration["MAIN_DB_CONNECTION_STRING"]!;
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        const string deleteSql = "DELETE FROM refresh_tokens WHERE user_id = @uid";
        await using var deleteCmd = new NpgsqlCommand(deleteSql, conn);
        deleteCmd.Parameters.AddWithValue("uid", Guid.Parse(userIdStr));
        await deleteCmd.ExecuteNonQueryAsync();

        return Ok(new { success = true });
    }
}
