using Swashbuckle.AspNetCore.Annotations;

namespace RagAuthService.Models;

[SwaggerSchema(Description = "Login request containing user credentials.")]
public record LoginRequest(string Email, string Password);

[SwaggerSchema(Description = "JWT tokens returned after successful authentication.")]
public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn);

[SwaggerSchema(Description = "Request to obtain new tokens using a refresh token.")]
public record RefreshRequest(string RefreshToken);

[SwaggerSchema(Description = "Request to verify the validity of a token.")]
public record IntrospectRequest(string Token);

[SwaggerSchema(Description = "User record retrieved from the authentication database.")]
public record User(Guid Id, string Email, string PasswordHash);
