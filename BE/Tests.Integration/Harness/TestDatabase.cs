using Npgsql;

namespace Tests.Integration.Harness;

/// <summary>
/// A PostgreSQL database owned exclusively by one test collection: created on
/// <see cref="CreateAsync"/>, dropped on <see cref="DisposeAsync"/>.
///
/// The only connection this type opens to anything other than its own database is an admin
/// connection to <c>postgres</c>, and the only statements it issues over it are CREATE/DROP
/// DATABASE for names starting with <see cref="TestConfig.DatabasePrefix"/>. The development
/// <c>chat</c> database is therefore unreachable from here by construction
/// (<c>automated-testing/test-isolation/development-data-untouched</c>).
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(string name)
    {
        Name = name;
        ConnectionString = TestConfig.ConnectionStringFor(name);
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<TestDatabase> CreateAsync(CancellationToken cancellationToken = default)
    {
        await DatabaseAvailability.EnsureAvailableAsync(cancellationToken);
        await ReclaimLeakedDatabasesAsync(cancellationToken);

        var name = TestConfig.DatabasePrefix + Guid.NewGuid().ToString("N");
        await using var admin = await OpenAdminAsync(cancellationToken);
        await ExecuteAsync(admin, $"CREATE DATABASE \"{name}\"", cancellationToken);

        return new TestDatabase(name);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Drop the pool first: an open connection of ours would block the DROP.
            NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));

            await using var admin = await OpenAdminAsync(CancellationToken.None);
            await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)", CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never fail a run over cleanup — the next run's reclaim pass will collect it.
            Console.Error.WriteLine($"[TestDatabase] Could not drop {Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Drops <c>chat_test_*</c> databases older than <see cref="TestConfig.LeakedDatabaseMaxAge"/>,
    /// so an interrupted run needs no manual cleanup
    /// (<c>automated-testing/test-isolation/aborted-run</c>).
    ///
    /// Age comes from the mtime of the database's <c>PG_VERSION</c> file, which is written when the
    /// database is created and never touched again. The age bound is what keeps this from dropping a
    /// database another run is actively using.
    /// </summary>
    private static async Task ReclaimLeakedDatabasesAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _reclaimed, 1) == 1) return;

        try
        {
            await using var admin = await OpenAdminAsync(cancellationToken);

            var stale = new List<string>();
            await using (var command = new NpgsqlCommand(
                             """
                             SELECT d.datname
                             FROM pg_database d
                             WHERE d.datname LIKE @prefix || '%'
                               AND NOT EXISTS (SELECT 1 FROM pg_stat_activity a WHERE a.datname = d.datname)
                               AND (pg_stat_file('base/' || d.oid || '/PG_VERSION')).modification < now() - @maxAge
                             """, admin))
            {
                command.Parameters.AddWithValue("prefix", TestConfig.DatabasePrefix);
                command.Parameters.AddWithValue("maxAge", TestConfig.LeakedDatabaseMaxAge);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) stale.Add(reader.GetString(0));
            }

            foreach (var name in stale)
            {
                try
                {
                    await ExecuteAsync(admin, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", cancellationToken);
                    Console.WriteLine($"[TestDatabase] Reclaimed leaked test database {name}.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[TestDatabase] Could not reclaim {name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // pg_stat_file needs superuser. If this server does not grant it, reclaiming is
            // simply unavailable — that must not stop the suite from running.
            Console.Error.WriteLine($"[TestDatabase] Leak reclaim pass skipped: {ex.Message}");
        }
    }

    private static int _reclaimed;

    private static async Task<NpgsqlConnection> OpenAdminAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(TestConfig.AdminConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
