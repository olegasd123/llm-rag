namespace RagAuthService.Models;

public record LoginRequest(string Email, string Password);
public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);
public record RefreshRequest(string RefreshToken);
public record IntrospectRequest(string Token);
public record User(int Id, string Email, string PasswordHash);
