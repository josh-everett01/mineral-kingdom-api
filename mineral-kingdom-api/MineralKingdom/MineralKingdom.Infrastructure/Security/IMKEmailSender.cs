namespace MineralKingdom.Infrastructure.Security;

public interface IMKEmailSender
{
  Task SendEmailVerificationAsync(string toEmail, string verificationLink, CancellationToken ct);
  Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken ct);
}
