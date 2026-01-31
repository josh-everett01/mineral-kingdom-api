using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MineralKingdom.Infrastructure.Configuration;
using MineralKingdom.Infrastructure.Persistence.Entities;

namespace MineralKingdom.Infrastructure.Security;

public sealed class JwtTokenService
{
  private readonly JwtOptions _opts;

  // Keep short-lived access tokens for S1-2
  public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);

  public JwtTokenService(IOptions<JwtOptions> opts)
  {
    _opts = opts.Value;
  }

  public (string token, int expiresInSeconds) CreateAccessToken(User user, DateTime utcNow)
  {
    if (string.IsNullOrWhiteSpace(_opts.Issuer) ||
        string.IsNullOrWhiteSpace(_opts.Audience) ||
        string.IsNullOrWhiteSpace(_opts.SigningKey))
    {
      throw new InvalidOperationException("MK_JWT config is missing.");
    }

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new List<Claim>
    {
      new(ClaimTypes.NameIdentifier, user.Id.ToString()),
      new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
      new(JwtRegisteredClaimNames.Email, user.Email),
      new("email_verified", user.EmailVerified ? "true" : "false"),
      new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(utcNow).ToString(), ClaimValueTypes.Integer64),
      new(ClaimTypes.Role, user.Role),
    };

    var expires = utcNow.Add(AccessTokenLifetime);

    var jwt = new JwtSecurityToken(
      issuer: _opts.Issuer,
      audience: _opts.Audience,
      claims: claims,
      notBefore: utcNow,
      expires: expires,
      signingCredentials: creds);

    var token = new JwtSecurityTokenHandler().WriteToken(jwt);
    return (token, (int)AccessTokenLifetime.TotalSeconds);
  }
}
