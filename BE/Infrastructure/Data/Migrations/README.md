# Identity Migrations

## Overview
Identity migrations are stored in the Infrastructure project under `Data/Migrations`.

## Automatic Migration on Startup
The application is configured to automatically apply pending migrations when it starts up. This is configured in `Program.cs`:

```csharp
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}
```

## Migration Files
- **20250101000000_InitialIdentity.cs** - Initial Identity schema migration
- **ApplicationDbContextModelSnapshot.cs** - Current database model snapshot

## Creating New Migrations

To create a new migration after making changes to the ApplicationDbContext or ApplicationUser:

```bash
# From the solution root directory
dotnet ef migrations add <MigrationName> --project Infrastructure --startup-project Web
```

## Applying Migrations Manually

If you need to manually apply migrations (though they apply automatically on startup):

```bash
dotnet ef database update --project Infrastructure --startup-project Web
```

## Database Configuration
The connection string is configured in `appsettings.json` or User Secrets:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=AndreyChat;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

## Initial Migration Contents
The initial migration creates all standard ASP.NET Core Identity tables:
- AspNetUsers (with custom DisplayName column)
- AspNetRoles
- AspNetUserRoles
- AspNetUserClaims
- AspNetUserLogins
- AspNetUserTokens
- AspNetRoleClaims
