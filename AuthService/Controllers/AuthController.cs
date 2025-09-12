using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
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
        var sessionKey = request.SessionKey;
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            sessionKey = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        }
        await reader.DisposeAsync();

        // If the current refresh token was expired we replace it with a new one (keeping session key same)
        const string insertSql = @"
            INSERT INTO refresh_tokens (user_id, token, expires_at, session_key)
            VALUES (@uid, @token, @exp, @sid)
            ON CONFLICT (user_id, session_key)
            DO UPDATE SET token = EXCLUDED.token,
                          expires_at = EXCLUDED.expires_at,
                          rotated_at = null";
        await using (var upsertCmd = new NpgsqlCommand(insertSql, conn))
        {
            upsertCmd.Parameters.AddWithValue("uid", user.Id);
            upsertCmd.Parameters.AddWithValue("token", TokenHashing.ComputeHash(refresh));
            upsertCmd.Parameters.AddWithValue("exp", DateTime.UtcNow.AddSeconds(_tokenService.RefreshTokenExpiryInSeconds));
            upsertCmd.Parameters.AddWithValue("sid", sessionKey);
            await upsertCmd.ExecuteNonQueryAsync();
        }

        // Return both tokens and the session key to the client
        return Ok(new TokenResponse(access, _tokenService.AccessTokenExpiryInSeconds, refresh, _tokenService.RefreshTokenExpiryInSeconds, sessionKey));
    }

    [HttpPost("refresh")]
    [SwaggerOperation(
        Summary = "Refresh tokens",
        Description = "Client sends refresh token. If it matches the server-stored token, rotate and return new access and refresh tokens. On mismatch, revoke and require re-login.")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        Console.WriteLine($"Token refresh attempt (client-sent refresh token)");

        if (string.IsNullOrWhiteSpace(request.SessionKey))
            return Unauthorized();

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

        // Load refresh token row by token hash and session key binding
        const string selectSql = "SELECT user_id, expires_at FROM refresh_tokens WHERE token = @token AND session_key = @sid";
        await using var selectCmd = new NpgsqlCommand(selectSql, conn);
        var providedHash = TokenHashing.ComputeHash(request.RefreshToken);
        selectCmd.Parameters.AddWithValue("token", providedHash);
        selectCmd.Parameters.AddWithValue("sid", request.SessionKey);
        await using var reader = await selectCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Unauthorized();

        var storedUserId = reader.GetGuid(0);
        var expiresAt = reader.GetDateTime(1);
        await reader.DisposeAsync();

        // Cross-check: token's subject must match row's user_id
        if (!Guid.TryParse(userIdStr, out var userId) || userId != storedUserId)
        {
            const string revokeSuspiciousSql = "DELETE FROM refresh_tokens WHERE token = @token";
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
            UPDATE refresh_tokens
            SET token = @newtoken, expires_at = @exp, rotated_at = now()
            WHERE user_id = @uid AND token = @oldtoken AND session_key = @sid";
        await using var updateCmd = new NpgsqlCommand(updateSql, conn);
        updateCmd.Parameters.AddWithValue("uid", userId);
        updateCmd.Parameters.AddWithValue("oldtoken", providedHash);
        updateCmd.Parameters.AddWithValue("newtoken", TokenHashing.ComputeHash(newRefresh));
        updateCmd.Parameters.AddWithValue("exp", DateTime.UtcNow.AddSeconds(_tokenService.RefreshTokenExpiryInSeconds));
        updateCmd.Parameters.AddWithValue("sid", request.SessionKey);
        await updateCmd.ExecuteNonQueryAsync();

        // Return new access and refresh tokens (same session key)
        return Ok(new TokenResponse(newAccess, _tokenService.AccessTokenExpiryInSeconds, newRefresh, _tokenService.RefreshTokenExpiryInSeconds, request.SessionKey));
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

    [HttpPost("logout-device")]
    [SwaggerOperation(Summary = "Logout current device", Description = "Revokes only the provided refresh token (device/session logout).")]
    public async Task<IActionResult> LogoutDevice([FromBody] LogoutDeviceRequest request)
    {
        Console.WriteLine("Logout current device attempt");

        // Validate token signature; ignore lifetime so expired tokens can be revoked
        var principal = _tokenService.ValidateToken(request.RefreshToken, validateLifetime: false);
        if (principal == null)
            return Unauthorized();

        var userIdStr = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var tokenHash = TokenHashing.ComputeHash(request.RefreshToken);

        var connString = _configuration["MAIN_DB_CONNECTION_STRING"]!;
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        const string deleteSql = "DELETE FROM refresh_tokens WHERE user_id = @uid AND token = @token";
        await using var deleteCmd = new NpgsqlCommand(deleteSql, conn);
        deleteCmd.Parameters.AddWithValue("uid", userId);
        deleteCmd.Parameters.AddWithValue("token", tokenHash);
        var affected = await deleteCmd.ExecuteNonQueryAsync();

        return Ok(new { success = affected > 0 });
    }
}
