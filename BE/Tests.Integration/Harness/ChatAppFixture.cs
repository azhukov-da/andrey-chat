using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;

namespace Tests.Integration.Harness;

/// <summary>
/// One database and one in-process application host, shared by every test in a collection.
///
/// xUnit runs the tests inside a collection sequentially and different collections in parallel,
/// so a fixture per collection gives isolation between concurrent groups
/// (<c>automated-testing/test-isolation</c>) without paying for a host per test. Between tests
/// <see cref="ResetAsync"/> truncates every table rather than re-migrating.
/// </summary>
public class ChatAppFixture : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Respawner _respawner = null!;

    public ChatAppFactory App { get; private set; } = null!;

    public string ConnectionString => _database.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();

        // Building the host runs Program.cs, which applies the migrations.
        App = await ChatAppFactory.CreateAsync(_database.ConnectionString);

        await AssertSchemaIsMigratedAsync();

        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            // The migration history is schema state, not test data — wiping it would make the
            // application think the database is unmigrated.
            TablesToIgnore = [new Respawn.Graph.Table("public", "__EFMigrationsHistory")],
        });
    }

    /// <summary>
    /// Returns the database to empty. Called before every test so each one starts from a known
    /// state and results do not depend on execution order
    /// (<c>automated-testing/test-isolation/order-independence</c>).
    /// </summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync()
    {
        App?.Dispose();
        if (_database is not null) await _database.DisposeAsync();
    }

    /// <summary>
    /// Program.cs logs and swallows migration failures rather than crashing the host, which would
    /// otherwise surface here as a wall of "relation does not exist" errors. Check once, loudly.
    /// </summary>
    private async Task AssertSchemaIsMigratedAsync()
    {
        await App.WithScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();

            if (applied.Count == 0 || pending.Count > 0)
            {
                throw new TestPrerequisiteException($"""
                    The test database {_database.Name} was not migrated by application startup.

                      Applied migrations: {applied.Count}
                      Pending migrations: {(pending.Count == 0 ? "none" : string.Join(", ", pending))}

                      Program.cs logs and swallows migration errors, so the cause is in the host's
                      startup output above. Most often the EF model and the migrations have diverged.
                    """);
            }
        });
    }
}

/// <summary>
/// The default collection. Capability suites that want their own database in parallel with this
/// one declare their own collection over <see cref="ChatAppFixture"/> the same way.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ChatAppCollection : ICollectionFixture<ChatAppFixture>
{
    public const string Name = "chat-app";
}
