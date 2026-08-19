using Application.Abstractions;
using Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Integration.Harness;

/// <summary>
/// Hosts the real <c>BE/Web</c> application in the test process, pointed at a test-owned
/// database. Requests go through the same middleware pipeline, the same controllers, the same
/// Identity endpoints, and the same <c>ChatHub</c> that production uses — only the transport
/// (an in-memory <c>TestServer</c>) and the database differ.
/// </summary>
public sealed class ChatAppFactory : WebApplicationFactory<Program>
{
    private const string ConnectionStringVariable = "ConnectionStrings__DefaultConnection";
    private const string UploadsRootVariable = "Uploads__Root";

    /// <summary>
    /// <c>Program.cs</c> resolves the connection string eagerly, before the host is built and
    /// therefore before any <see cref="ConfigureWebHost"/> configuration source is visible. The
    /// only channel that lands early enough is the process environment, which is global — so
    /// host construction is serialised and the variables are restored afterwards. Only
    /// construction is serialised; the tests themselves still run in parallel.
    /// </summary>
    private static readonly SemaphoreSlim BuildGate = new(1, 1);

    private readonly string _connectionString;
    private readonly string _uploadsRoot;

    private ChatAppFactory(string connectionString, string uploadsRoot)
    {
        _connectionString = connectionString;
        _uploadsRoot = uploadsRoot;
    }

    /// <summary>Uploads written by tests land here rather than in the app's configured root.</summary>
    public string UploadsRoot => _uploadsRoot;

    public static async Task<ChatAppFactory> CreateAsync(string connectionString)
    {
        var uploadsRoot = Path.Combine(Path.GetTempPath(), "andrey-chat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(uploadsRoot);

        var factory = new ChatAppFactory(connectionString, uploadsRoot);

        await BuildGate.WaitAsync();
        var previousConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        var previousUploadsRoot = Environment.GetEnvironmentVariable(UploadsRootVariable);
        try
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, connectionString);
            Environment.SetEnvironmentVariable(UploadsRootVariable, uploadsRoot);

            // Touching Services runs the entry point and builds the host, which is what has to
            // happen while the environment variables above are in place. Program.cs applies the
            // EF migrations during this call, so the test database is fully schema-current when
            // this returns — via the application's own startup path, not a test-only shortcut.
            _ = factory.Services;
        }
        finally
        {
            Environment.SetEnvironmentVariable(ConnectionStringVariable, previousConnectionString);
            Environment.SetEnvironmentVariable(UploadsRootVariable, previousUploadsRoot);
            BuildGate.Release();
        }

        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: appsettings.Development.json is a developer's local file and the suite
        // must not inherit whatever is in it. Nothing in the app requires Development except the
        // Swagger UI, which no test exercises.
        builder.UseEnvironment("Testing");

        builder.UseSetting($"ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("Uploads:Root", _uploadsRoot);

        builder.ConfigureServices(services =>
        {
            // Belt and braces for the eager-configuration problem described on BuildGate: if the
            // registration ever stops depending on the environment variable, this still wins
            // because ConfigureServices runs after every registration Program.cs makes.
            services.AddSingleton<IFileStorage>(new LocalFileStorage(_uploadsRoot));
        });
    }

    /// <summary>
    /// Runs <paramref name="work"/> against a scoped service provider — the way a request would
    /// see the DI graph. Use for arranging state and for asserting on it directly.
    /// </summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> work)
    {
        using var scope = Services.CreateScope();
        await work(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            if (Directory.Exists(_uploadsRoot)) Directory.Delete(_uploadsRoot, recursive: true);
        }
        catch
        {
            // A leftover temp directory is not worth failing a run over.
        }
    }
}
