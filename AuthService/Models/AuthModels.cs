using Swashbuckle.AspNetCore.Annotations;

namespace RagAuthService.Models;

[SwaggerSchema(Description = "Login request containing user credentials.")]
public record LoginRequest(string Email, string Password);

[SwaggerSchema(Description = "JWT access token returned after successful authentication or refresh.")]
public record TokenResponse(string AccessToken, int ExpiresIn);

[SwaggerSchema(Description = "Request to obtain a new access token using a user's access token (can be expired). Refresh is handled server-side.")]
public record RefreshRequest(string Token);

[SwaggerSchema(Description = "Request to verify the validity of a token.")]
public record IntrospectRequest(string Token);

[SwaggerSchema(Description = "Request to logout (revoke server-side refresh token) using an access token, which may be expired.")]
public record LogoutRequest(string Token);

[SwaggerSchema(Description = "User record retrieved from the authentication database.")]
public record User(Guid Id, string Email, string PasswordHash);
