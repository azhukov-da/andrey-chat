using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.UserSessions;

/// <summary>
/// Backend integration tests for <c>openspec/specs/user-sessions/spec.md</c> — the parts of that
/// capability the server enforces regardless of which client calls it: what the session list
/// contains and orders by, whose sessions a caller can see, and who may revoke what.
///
/// Everything goes through <c>/api/Sessions</c> over real HTTP with real Identity tokens; no test
/// calls <c>ISessionService</c> directly.
/// </summary>
public class SessionsTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    // --- Listing ---

    [Fact(DisplayName = UserSessionsSpec.ViewingSessions
                        + "lists every active session most-recently-seen first, with device details")]
    public async Task Lists_sessions_newest_seen_first_with_device_details()
    {
        var user = await CreateUserAsync(deviceInfo: "Desktop", userAgent: "Firefox/1.0");
        await AuthHelper.RegisterSessionAsync(user.Client, "Tablet");
        await AuthHelper.RegisterSessionAsync(user.Client, "Phone");

        var sessions = await ListSessionsAsync(user);

        sessions.Should().HaveCount(3);
        sessions.Select(s => s.DeviceInfo).Should().Equal(
            ["Phone", "Tablet", "Desktop"],
            "the list is ordered by last-seen descending, and each session was seen when it was registered");

        var desktop = sessions.Single(s => s.DeviceInfo == "Desktop");
        desktop.UserAgent.Should().Be("Firefox/1.0");
        desktop.CreatedAt.Should().NotBe(default);
        desktop.LastSeenAt.Should().NotBe(default);
        desktop.IsCurrent.Should().BeTrue("the caller's X-Session-Id names the session it registered first");

        sessions.Where(s => s.DeviceInfo != "Desktop").Should()
            .OnlyContain(s => !s.IsCurrent, "only one session can be the caller's own");
    }

    [Fact(DisplayName = UserSessionsSpec.OnlyOwnSessions
                        + "a user never sees another account's sessions")]
    public async Task Never_returns_another_accounts_sessions()
    {
        var alice = await CreateUserAsync(deviceInfo: "Alice's laptop");
        var bob = await CreateUserAsync(deviceInfo: "Bob's laptop");

        var aliceSessions = await ListSessionsAsync(alice);
        var bobSessions = await ListSessionsAsync(bob);

        aliceSessions.Should().OnlyContain(s => s.DeviceInfo == "Alice's laptop");
        bobSessions.Should().OnlyContain(s => s.DeviceInfo == "Bob's laptop");
        aliceSessions.Select(s => s.Id).Should().NotIntersectWith(bobSessions.Select(s => s.Id));
    }

    // --- Registration ---

    [Fact(DisplayName = UserSessionsSpec.NewSignInRegistersASession
                        + "registration records the device description, user agent, and both timestamps")]
    public async Task Registration_records_the_device_details_it_was_given()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);

        var user = await CreateUserAsync(deviceInfo: "Win32", userAgent: "AndreyChat-Test/2.0");

        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().UserSessions
                .SingleAsync(s => s.Id == user.SessionId!.Value));

        stored.UserId.Should().Be(user.UserId);
        stored.DeviceInfo.Should().Be("Win32");
        stored.UserAgent.Should().Be("AndreyChat-Test/2.0");
        stored.CreatedAt.Should().BeAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        stored.LastSeenAt.Should().BeOnOrAfter(stored.CreatedAt);
        stored.RevokedAt.Should().BeNull();
    }

    [Fact(DisplayName = UserSessionsSpec.NewSignInRegistersASession
                        + "registration records the originating IP address the connection reports")]
    public async Task Registration_records_the_originating_ip_address()
    {
        var user = await CreateUserAsync();

        // TestServer leaves Connection.RemoteIpAddress unset unless a test supplies it, so the
        // request is built through Server.SendAsync — the one place the harness has to stand in for
        // the transport. What is under test is still the server's own behaviour: that the controller
        // reads the connection's address and the service persists it.
        await App.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/Sessions/register";
            context.Request.Headers.Authorization = $"Bearer {user.AccessToken}";
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(
                Encoding.UTF8.GetBytes("""{"deviceInfo":"Device behind a known address"}"""));
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        });

        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().UserSessions
                .SingleAsync(s => s.DeviceInfo == "Device behind a known address"));

        stored.IpAddress.Should().Be("203.0.113.7",
            "the sessions screen shows the IP a session came from, so registration has to capture it");
    }

    // --- Revoking ---

    [Fact(DisplayName = UserSessionsSpec.RevokingAnotherSession
                        + "revoking marks the session revoked and drops it from the list")]
    public async Task Revoking_marks_the_session_revoked_and_removes_it_from_the_list()
    {
        var user = await CreateUserAsync(deviceInfo: "Current");
        var other = await AuthHelper.RegisterSessionAsync(user.Client, "Old phone");

        var response = await user.Client.DeleteAsync($"/api/Sessions/{other}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().UserSessions
                .SingleAsync(s => s.Id == other));
        stored.RevokedAt.Should().NotBeNull("revoking is a soft delete, so the row stays");

        var remaining = await ListSessionsAsync(user);
        remaining.Should().OnlyContain(s => s.DeviceInfo == "Current");
    }

    [Fact(DisplayName = UserSessionsSpec.RevokingAForeignSession
                        + "revoking a session belonging to another account is rejected and revokes nothing")]
    public async Task Revoking_another_accounts_session_is_rejected_as_not_found()
    {
        var alice = await CreateUserAsync(deviceInfo: "Alice's laptop");
        var bob = await CreateUserAsync(deviceInfo: "Bob's laptop");

        var response = await alice.Client.DeleteAsync($"/api/Sessions/{bob.SessionId}");

        response.StatusCode.Should().NotBe(HttpStatusCode.NoContent);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("not found",
            "the caller must not be able to tell a foreign session from a nonexistent one");

        var bobsSession = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().UserSessions
                .SingleAsync(s => s.Id == bob.SessionId!.Value));
        bobsSession.RevokedAt.Should().BeNull("nothing was revoked");

        (await ListSessionsAsync(bob)).Should().HaveCount(1, "Bob's session still works");
    }

    private static async Task<List<SessionResponse>> ListSessionsAsync(TestUser user)
    {
        var response = await user.Client.GetAsync("/api/Sessions");
        response.IsSuccessStatusCode.Should().BeTrue(
            $"listing sessions failed: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<List<SessionResponse>>())!;
    }

    /// <summary>The shape <c>GET /api/Sessions</c> returns, as a client sees it.</summary>
    private sealed record SessionResponse(
        Guid Id,
        string? DeviceInfo,
        string? UserAgent,
        string? IpAddress,
        DateTime CreatedAt,
        DateTime LastSeenAt,
        bool IsCurrent);
}
