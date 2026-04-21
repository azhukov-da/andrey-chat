using Application;
using Application.Abstractions;
using Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Configuration;
using Web.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection("Cors"));
var corsOptions = builder.Configuration.GetSection("Cors").Get<CorsOptions>() ?? new CorsOptions();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOptions.AllowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddIdentityApiEndpoints<ApplicationUser>()
    .AddEntityFrameworkStores<Infrastructure.Data.ApplicationDbContext>();

//builder.Services.AddAuthentication().AddBearerToken(IdentityConstants.BearerScheme);

builder.Services.AddAuthorizationBuilder();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        // Hide 2FA-related endpoints
        var routeTemplate = apiDesc.RelativePath ?? string.Empty;
        return !routeTemplate.Contains("manage/2fa") && 
               !routeTemplate.Contains("manage/info");
    });
});

builder.Services.AddSingleton<IPostConfigureOptions<BearerTokenOptions>>(
    new SignalRBearerTokenOptions(IdentityConstants.BearerScheme));

builder.Services.AddSignalR();

builder.Services.AddSingleton<IChatNotifier, ChatNotifier>();

builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<IApplicationDbContext>();
        if (context is Infrastructure.Data.ApplicationDbContext dbContext)
        {
            await dbContext.Database.MigrateAsync();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapIdentityApi<ApplicationUser>().WithTags("Auth");
app.MapHub<ChatHub>("/hubs/chat");

app.Run();



