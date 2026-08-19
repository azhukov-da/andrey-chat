using Npgsql;

namespace Tests.Integration.Harness;

/// <summary>
/// Fail-fast reachability check for the PostgreSQL server the suite depends on.
///
/// Satisfies <c>automated-testing/test-isolation/database-unavailable</c>: the run stops
/// once with a message naming the expected endpoint and how to start it, instead of every
/// test failing separately with a connection error.
/// </summary>
public static class DatabaseAvailability
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _checked;
    private static string? _failure;

    public static async Task EnsureAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_checked)
        {
            if (_failure is not null) throw new TestPrerequisiteException(_failure);
            return;
        }

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (!_checked)
            {
                _failure = await ProbeAsync(cancellationToken);
                _checked = true;
            }
        }
        finally
        {
            Gate.Release();
        }

        if (_failure is not null) throw new TestPrerequisiteException(_failure);
    }

    private static async Task<string?> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(TestConfig.AdminConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            return $"""
                Backend integration tests need PostgreSQL at {TestConfig.Endpoint} and could not reach it.

                  Start it with:
                    {TestConfig.StartCommand}

                  The port is published by the test-only overlay docker-compose.tests.yml, not by
                  docker-compose.yml, so a plain `docker compose up` will not expose it.

                  Override the endpoint with TEST_DB_HOST / TEST_DB_PORT / TEST_DB_USER / TEST_DB_PASSWORD.

                  Underlying error: {ex.GetType().Name}: {ex.Message}
                """;
        }
    }
}

/// <summary>Thrown when something the suite depends on is not running. The message is the whole point.</summary>
public sealed class TestPrerequisiteException(string message) : Exception(message);
