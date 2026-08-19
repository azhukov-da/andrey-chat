using System.Text.Json;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.UserPresence;

/// <summary>
/// Backend integration tests for presence — <c>openspec/specs/user-presence/spec.md</c>.
///
/// The server owns the classification: it decides from the heartbeats it has received which of the
/// three states a user is in, aggregates across a user's connections, and pushes transitions. What
/// the client decides — when to report itself inactive, which tab does the heartbeating — is
/// verified at the frontend layer.
/// </summary>
[Collection(Collections.Realtime)]
public class PresenceTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = UserPresenceSpec.ConnectedAndActive
                        + "a connection reporting activity puts the user online")]
    public async Task An_active_connection_is_online()
    {
        var user = await CreateUserAsync();
        var observer = await CreateUserAsync();

        await using var hub = await ConnectHubAsync(user);
        await hub.InvokeAsync("Ping", true);

        await using var observing = await ConnectHubAsync(observer);
        (await LookUpAsync(observing, user)).Should().Be("Online");
    }

    [Fact(DisplayName = UserPresenceSpec.NoConnection
                        + "a user with no live connection is offline")]
    public async Task A_user_with_no_connection_is_offline()
    {
        var absent = await CreateUserAsync();
        var observer = await CreateUserAsync();

        await using var observing = await ConnectHubAsync(observer);

        (await LookUpAsync(observing, absent)).Should().Be("Offline",
            "never having connected is indistinguishable from having gone");
    }

    [Fact(DisplayName = UserPresenceSpec.ClosingTheLastTab
                        + "a user goes offline when their last connection ends")]
    public async Task Losing_the_last_connection_goes_offline()
    {
        var user = await CreateUserAsync();
        var observer = await CreateUserAsync();
        await using var observing = await ConnectHubAsync(observer);

        var hub = await ConnectHubAsync(user);
        await hub.InvokeAsync("Ping", true);
        (await LookUpAsync(observing, user)).Should().Be("Online");

        await hub.DisposeAsync();
        await PresenceChangeForAsync(observing, user);

        (await LookUpAsync(observing, user)).Should().Be("Offline");
    }

    [Fact(DisplayName = UserPresenceSpec.TwoTabsOpen
                        + "a user with two connections stays online, and stays online when one ends")]
    public async Task Presence_is_aggregated_across_a_users_connections()
    {
        var user = await CreateUserAsync();
        var second = await AuthHelper.SignInAgainAsync(Fixture, user);
        var observer = await CreateUserAsync();
        await using var observing = await ConnectHubAsync(observer);

        var firstTab = await ConnectHubAsync(user);
        await using var secondTab = await ConnectHubAsync(second);
        await firstTab.InvokeAsync("Ping", true);
        await secondTab.InvokeAsync("Ping", true);

        (await LookUpAsync(observing, user)).Should().Be("Online");

        await firstTab.DisposeAsync();

        (await LookUpAsync(observing, user)).Should().Be("Online",
            "one connection ending is not the account going away, so long as another remains");
    }

    [Fact(DisplayName = UserPresenceSpec.GoingIdle
                        + "a heartbeat reporting inactivity moves the user out of online")]
    public async Task Reporting_inactive_moves_the_user_out_of_online()
    {
        var user = await CreateUserAsync();
        var observer = await CreateUserAsync();
        await using var observing = await ConnectHubAsync(observer);

        await using var hub = await ConnectHubAsync(user);
        await hub.InvokeAsync("Ping", true);
        (await LookUpAsync(observing, user)).Should().Be("Online");

        await hub.InvokeAsync("Ping", false);
        await PresenceChangeForAsync(observing, user);

        (await LookUpAsync(observing, user)).Should().NotBe("Online",
            "the client reports inactivity and the server stops calling them online");
    }

    [Fact(Skip = "SPEC GAP user-presence/presence-states: the idle state is reported as \"Away\", not "
                 + "\"afk\" as the specification names it. See docs/spec-gaps.md.",
          DisplayName = UserPresenceSpec.ConnectedButIdle
                        + "a connection reporting no activity puts the user in the afk state")]
    public async Task An_inactive_connection_is_afk()
    {
        var user = await CreateUserAsync();
        var observer = await CreateUserAsync();
        await using var observing = await ConnectHubAsync(observer);

        await using var hub = await ConnectHubAsync(user);
        await hub.InvokeAsync("Ping", false);

        (await LookUpAsync(observing, user)).ToLowerInvariant().Should().Be("afk",
            "the specification names exactly three states: online, afk, and offline");
    }

    [Fact(DisplayName = UserPresenceSpec.ReturningFromIdle
                        + "activity after a quiet spell puts the user back online")]
    public async Task Reporting_active_again_returns_the_user_to_online()
    {
        var user = await CreateUserAsync();
        var observer = await CreateUserAsync();
        await using var observing = await ConnectHubAsync(observer);

        await using var hub = await ConnectHubAsync(user);
        await hub.InvokeAsync("Ping", false);
        (await LookUpAsync(observing, user)).Should().NotBe("Online");

        await hub.InvokeAsync("Ping", true);
        await PresenceChangeForAsync(observing, user);

        (await LookUpAsync(observing, user)).Should().Be("Online");
    }

    [Fact(DisplayName = UserPresenceSpec.ContactComesOnline
                        + "an observer is pushed a presence change when someone connects")]
    public async Task Connecting_pushes_a_presence_change_to_observers()
    {
        var observer = await CreateUserAsync();
        var arriving = await CreateUserAsync();

        await using var observing = await ConnectHubAsync(observer);

        await using var hub = await ConnectHubAsync(arriving);

        var payload = await PresenceChangeForAsync(observing, arriving);
        payload.GetProperty("userId").GetString().Should().Be(arriving.UserId);
        payload.GetProperty("status").GetString().Should().Be("Online",
            "the push carries the new state, so observers converge without asking");
    }

    [Fact(DisplayName = UserPresenceSpec.ContactDisconnects
                        + "an observer is pushed a presence change when someone's connection drops")]
    public async Task Disconnecting_pushes_a_presence_change_to_observers()
    {
        var observer = await CreateUserAsync();
        var leaving = await CreateUserAsync();
        await using var observing = await ConnectHubAsync(observer);

        var hub = await ConnectHubAsync(leaving);
        await PresenceChangeForAsync(observing, leaving);

        await hub.DisposeAsync();

        var payload = await PresenceChangeForAsync(observing, leaving);
        payload.GetProperty("userId").GetString().Should().Be(leaving.UserId);
        payload.GetProperty("status").GetString().Should().Be("Offline");
    }

    [Fact(DisplayName = UserPresenceSpec.HydratingAFreshlyOpenedView
                        + "one call answers the state of a set of users, including those never seen")]
    public async Task A_bulk_lookup_answers_for_every_user_asked_about()
    {
        var observer = await CreateUserAsync();
        var online = await CreateUserAsync();
        var offline = await CreateUserAsync();

        await using var onlineHub = await ConnectHubAsync(online);
        await onlineHub.InvokeAsync("Ping", true);

        await using var observing = await ConnectHubAsync(observer);
        var statuses = await observing.InvokeAsync<Dictionary<string, string>>(
            "GetPresenceFor", (object)new[] { online.UserId, offline.UserId });

        statuses.Should().ContainKey(online.UserId).WhoseValue.Should().Be("Online");
        statuses.Should().ContainKey(offline.UserId).WhoseValue.Should().Be("Offline",
            "a view hydrating itself needs an answer for everyone it asks about, not only the present");
    }

    [Fact(DisplayName = UserPresenceSpec.AfterReconnect
                        + "a lookup after reconnecting returns current state, not what was true before")]
    public async Task A_lookup_after_reconnecting_is_current()
    {
        var observer = await CreateUserAsync();
        var other = await CreateUserAsync();

        var otherHub = await ConnectHubAsync(other);
        await otherHub.InvokeAsync("Ping", true);

        var firstConnection = await ConnectHubAsync(observer);
        (await LookUpAsync(firstConnection, other)).Should().Be("Online");
        await firstConnection.DisposeAsync();

        // While the observer is away, the other user goes.
        await otherHub.DisposeAsync();

        await using var reconnected = await ConnectHubAsync(observer);
        (await LookUpAsync(reconnected, other)).Should().Be("Offline",
            "the lookup is answered from current state, which is the point of hydrating on reconnect");
    }

    [Fact(DisplayName = UserPresenceSpec.HydratingAFreshlyOpenedView
                        + "a lookup for nobody returns nothing rather than failing")]
    public async Task An_empty_lookup_is_answered_with_an_empty_map()
    {
        var observer = await CreateUserAsync();
        await using var observing = await ConnectHubAsync(observer);

        var statuses = await observing.InvokeAsync<Dictionary<string, string>>(
            "GetPresenceFor", (object)Array.Empty<string>());

        statuses.Should().BeEmpty();
    }

    /// <summary>
    /// Waits for the next presence change <em>about</em> <paramref name="subject"/>.
    /// <c>PresenceChangedAsync</c> broadcasts to every connection, so an observer sees its own
    /// arrival and everyone else's too; a bare expectation would frequently claim the wrong one.
    /// </summary>
    private static async Task<JsonElement> PresenceChangeForAsync(
        TestHubClient observer, TestUser subject, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var payload = await observer.Expect<JsonElement>(
                ChatHubEvents.PresenceChanged, deadline - DateTime.UtcNow).RawAsync();
            if (payload.GetProperty("userId").GetString() == subject.UserId) return payload;
        }

        throw new TimeoutException($"No PresenceChanged for {subject.Username} arrived in time.");
    }

    private static async Task<string> LookUpAsync(TestHubClient observer, TestUser subject)
    {
        // The cast matters: InvokeAsync takes params object?[], so an unboxed string[] would be
        // splatted into one argument per element instead of arriving as the single string[] the hub
        // method declares.
        var statuses = await observer.InvokeAsync<Dictionary<string, string>>(
            "GetPresenceFor", (object)new[] { subject.UserId });
        return statuses[subject.UserId];
    }
}
