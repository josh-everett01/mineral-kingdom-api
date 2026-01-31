using Microsoft.Extensions.Logging;

namespace MineralKingdom.Infrastructure.Security;

public sealed class DevNullEmailSender : IMKEmailSender
{
  private readonly ILogger<DevNullEmailSender> _logger;

  public DevNullEmailSender(ILogger<DevNullEmailSender> logger)
  {
    _logger = logger;
  }

  public Task SendEmailVerificationAsync(string email, string verificationLink, CancellationToken ct)
  {
    _logger.LogInformation("Email verification for {Email}: {Link}", email, verificationLink);
    return Task.CompletedTask;
  }

  public Task SendPasswordResetAsync(string email, string resetLink, CancellationToken ct)
  {
    _logger.LogInformation("Password reset for {Email}: {Link}", email, resetLink);
    return Task.CompletedTask;
  }
}
