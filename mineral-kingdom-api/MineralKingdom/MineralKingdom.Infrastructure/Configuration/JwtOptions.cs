namespace MineralKingdom.Infrastructure.Configuration;

public class JwtOptions
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = "";
}
