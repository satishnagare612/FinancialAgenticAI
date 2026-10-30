using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FinancialAI.Api.Auth;

/// <summary>
/// Issues HS256-signed JWTs for local development only. The token is
/// accepted by the JwtBearer middleware configured in Program.cs — the
/// same middleware that would validate tokens from a real OIDC provider
/// in production. The only difference between dev and prod is the key
/// source; the claim shape and validation logic are identical.
/// </summary>
public static class DevTokenIssuer
{
    public static string Issue(string userId, IConfiguration configuration)
    {
        var signingKey = configuration["Auth:SigningKey"]
            ?? throw new InvalidOperationException(
                "Auth:SigningKey is required. For local development set it in " +
                "appsettings.Development.json. For production, source it from " +
                "Key Vault or an equivalent secret manager.");

        var issuer = configuration["Auth:Issuer"] ?? "FinancialAI";
        var audience = configuration["Auth:Audience"] ?? "FinancialAI";

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userId),
            new Claim(JwtRegisteredClaimNames.Sub, userId),
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
