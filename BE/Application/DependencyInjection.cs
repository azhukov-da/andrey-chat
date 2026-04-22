using Application.Abstractions;
using Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddValidatorsFromAssembly(assembly);

        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IDirectChatService, DirectChatService>();
        services.AddScoped<IFriendService, FriendService>();
        services.AddScoped<ISessionService, SessionService>();

        return services;
    }
}

