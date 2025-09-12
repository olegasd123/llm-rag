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
        await reader.DisposeAsync();

        // Upsert a single refresh token per user
        const string insertSql = @"
            INSERT INTO auth_refresh_tokens (user_id, token, expires_at)
            VALUES (@uid, @token, @exp)
            ON CONFLICT (user_id)
            DO UPDATE SET token = EXCLUDED.token,
                          expires_at = EXCLUDED.expires_at,
                          rotated_at = null";
        await using (var upsertCmd = new NpgsqlCommand(insertSql, conn))
        {
            upsertCmd.Parameters.AddWithValue("uid", user.Id);
            upsertCmd.Parameters.AddWithValue("token", TokenHashing.ComputeHash(refresh));
            upsertCmd.Parameters.AddWithValue("exp", DateTime.UtcNow.AddSeconds(_tokenService.RefreshTokenExpiryInSeconds));
            await upsertCmd.ExecuteNonQueryAsync();
        }

        // Return tokens
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

        // Load refresh token row by user and current token hash
        const string selectSql = "SELECT user_id, expires_at FROM auth_refresh_tokens WHERE user_id = @uid AND token = @token";
        await using var selectCmd = new NpgsqlCommand(selectSql, conn);
        var providedHash = TokenHashing.ComputeHash(request.RefreshToken);
        selectCmd.Parameters.AddWithValue("uid", Guid.Parse(userIdStr));
        selectCmd.Parameters.AddWithValue("token", providedHash);
        await using var reader = await selectCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Unauthorized();

        var storedUserId = reader.GetGuid(0);
        var expiresAt = reader.GetDateTime(1);
        await reader.DisposeAsync();

        // Cross-check: token's subject must match row's user_id
        if (!Guid.TryParse(userIdStr, out var userId) || userId != storedUserId)
        {
            const string revokeSuspiciousSql = "DELETE FROM auth_refresh_tokens WHERE token = @token";
            await using var revokeSuspiciousCmd = new NpgsqlCommand(revokeSuspiciousSql, conn);
            revokeSuspiciousCmd.Parameters.AddWithValue("token", providedHash);
            await revokeSuspiciousCmd.ExecuteNonQueryAsync();
            return Unauthorized();
        }

        if (expiresAt <= DateTime.UtcNow)
            return Unauthorized();

        // Rotate refresh token and issue new access token
        var newAccess = _tokenService.GenerateAccessToken(userIdStr, email);
        var newRefresh = _tokenService.GenerateRefreshToken(userIdStr, email);

        const string updateSql = @"
            UPDATE auth_refresh_tokens
            SET token = @newtoken, expires_at = @exp, rotated_at = now()
            WHERE user_id = @uid AND token = @oldtoken";
        await using var updateCmd = new NpgsqlCommand(updateSql, conn);
        updateCmd.Parameters.AddWithValue("uid", userId);
        updateCmd.Parameters.AddWithValue("oldtoken", providedHash);
        updateCmd.Parameters.AddWithValue("newtoken", TokenHashing.ComputeHash(newRefresh));
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

        const string deleteSql = "DELETE FROM auth_refresh_tokens WHERE user_id = @uid";
        await using var deleteCmd = new NpgsqlCommand(deleteSql, conn);
        deleteCmd.Parameters.AddWithValue("uid", Guid.Parse(userIdStr));
        await deleteCmd.ExecuteNonQueryAsync();

        return Ok(new { success = true });
    }
}
