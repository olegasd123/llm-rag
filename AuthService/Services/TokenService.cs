using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RagAuthService;

public class TokenService
{
    private readonly string _secret;

    public int AccessTokenExpiryInSeconds { get; }
    public int RefreshTokenExpiryInSeconds { get; }

    public TokenService(string secret, int accessTokenExpiryInSeconds = 15 * 60, int refreshTokenExpiryInSeconds = 7 * 24 * 60 * 60)
    {
        _secret = secret;
        AccessTokenExpiryInSeconds = accessTokenExpiryInSeconds;
        RefreshTokenExpiryInSeconds = refreshTokenExpiryInSeconds;
    }

    private SymmetricSecurityKey GetSigningKey() => new(Encoding.UTF8.GetBytes(_secret));

    private string GenerateToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var creds = new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(claims: claims, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateAccessToken(string userId, string email)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, email)
        };
        return GenerateToken(claims, DateTime.UtcNow.AddSeconds(AccessTokenExpiryInSeconds));
    }

    public string GenerateRefreshToken(string userId, string email)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim("typ", "refresh")
        };
        return GenerateToken(claims, DateTime.UtcNow.AddSeconds(RefreshTokenExpiryInSeconds));
    }

    public ClaimsPrincipal? ValidateToken(string token, bool validateLifetime = true)
    {
        var tokenHandler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        tokenHandler.InboundClaimTypeMap.Clear();
        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = GetSigningKey(),
                ValidateLifetime = validateLifetime,
                ClockSkew = TimeSpan.Zero
            }, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}
