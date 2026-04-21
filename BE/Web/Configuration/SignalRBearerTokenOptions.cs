using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.Extensions.Options;

namespace Web.Configuration;

public class SignalRBearerTokenOptions : IPostConfigureOptions<BearerTokenOptions>
{
    private readonly string _schemeName;

    public SignalRBearerTokenOptions(string schemeName)
    {
        _schemeName = schemeName;
    }

    public void PostConfigure(string? name, BearerTokenOptions options)
    {
        if (name != _schemeName) return;

        var onMessageReceived = options.Events.OnMessageReceived;
        options.Events.OnMessageReceived = async context =>
        {
            var token = context.Request.Query["access_token"].FirstOrDefault();
            if (!string.IsNullOrEmpty(token) && context.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Token = token;
            }

            if (onMessageReceived != null)
                await onMessageReceived(context);
        };
    }
}
