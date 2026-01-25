namespace MineralKingdom.Infrastructure.Persistence;

public class DbPing
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
