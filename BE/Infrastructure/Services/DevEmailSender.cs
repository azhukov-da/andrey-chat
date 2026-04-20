using Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class DevEmailSender : IEmailSender
{
    private readonly ILogger<DevEmailSender> _logger;

    public DevEmailSender(ILogger<DevEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "DEV EMAIL: To={To}, Subject={Subject}, Body={Body}",
            to,
            subject,
            body);

        return Task.CompletedTask;
    }
}
