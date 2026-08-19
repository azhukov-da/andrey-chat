using Npgsql;

namespace Tests.Integration.Harness;

/// <summary>
/// Where the integration suite expects to find PostgreSQL, and how to name the
/// databases it owns. Defaults match <c>docker-compose.tests.yml</c>; every value can
/// be overridden by environment variable so a developer with a different local setup
/// does not have to edit code.
/// </summary>
public static class TestConfig
{
    /// <summary>Every database this suite creates starts with this prefix. Nothing outside
    /// the prefix is ever touched — in particular not the development <c>chat</c> database.</summary>
    public const string DatabasePrefix = "chat_test_";

    /// <summary>Databases matching <see cref="DatabasePrefix"/> older than this are reclaimed
    /// on startup, so an aborted run leaks at most one database until the next day's run.</summary>
    public static readonly TimeSpan LeakedDatabaseMaxAge = TimeSpan.FromHours(24);

    public static string Host => Environment.GetEnvironmentVariable("TEST_DB_HOST") ?? "localhost";

    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("TEST_DB_PORT"), out var p) ? p : 55432;

    public static string Username => Environment.GetEnvironmentVariable("TEST_DB_USER") ?? "postgres";

    public static string Password => Environment.GetEnvironmentVariable("TEST_DB_PASSWORD") ?? "postgres";

    /// <summary>Human-readable endpoint used in failure messages.</summary>
    public static string Endpoint => $"{Host}:{Port}";

    /// <summary>
    /// Connection to the server's own <c>postgres</c> database. Used only for
    /// <c>CREATE DATABASE</c> / <c>DROP DATABASE</c> against <see cref="DatabasePrefix"/> names.
    /// </summary>
    public static string AdminConnectionString => Build("postgres");

    public static string ConnectionStringFor(string database) => Build(database);

    private static string Build(string database) => new NpgsqlConnectionStringBuilder
    {
        Host = Host,
        Port = Port,
        Database = database,
        Username = Username,
        Password = Password,
        // Pooling is per connection string, so each test database gets its own pool;
        // keep it small because a run may hold several databases open at once.
        MaxPoolSize = 20,
        Timeout = 15,
    }.ConnectionString;

    /// <summary>The command that makes <see cref="Endpoint"/> reachable, quoted in failure messages.</summary>
    public const string StartCommand =
        "docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db";
}
