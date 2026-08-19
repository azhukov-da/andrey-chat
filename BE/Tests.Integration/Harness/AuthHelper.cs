using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Integration.Harness;

/// <summary>
/// Creates authenticated users the way a browser does: register over
/// <c>POST /api/auth/register</c>, sign in over <c>POST /api/auth/login</c>, then register a
/// session over <c>POST /api/Sessions/register</c>. No test reaches into Identity directly, so
/// what the tests exercise is the same path a client takes.
/// </summary>
public static class AuthHelper
{
    /// <summary>Satisfies the password policy configured in <c>Infrastructure.DependencyInjection</c>.</summary>
    public const string DefaultPassword = "Passw0rd!";

    /// <summary>
    /// Registers a brand-new user, signs them in, and registers a session for them.
    /// </summary>
    /// <param name="fixture">The collection's fixture.</param>
    /// <param name="deviceInfo">Device description sent to session registration.</param>
    /// <param name="userAgent">User-Agent header sent on every request this user makes.</param>
    /// <param name="registerSession">
    /// False to get a signed-in user with no session at all — needed to test the behaviour of
    /// requests that arrive without an <c>X-Session-Id</c> header.
    /// </param>
    public static async Task<TestUser> CreateUserAsync(
        ChatAppFixture fixture,
        string? deviceInfo = "Integration test device",
        string? userAgent = "IntegrationTests/1.0",
        bool registerSession = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var email = $"user-{suffix}@example.test";
        var username = $"user{suffix}";

        var client = fixture.App.CreateClient();
        if (userAgent is not null) client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        var registration = await client.PostAsJsonAsync(
            "/api/auth/register", new { email, username, password = DefaultPassword });
        await EnsureSuccessAsync(registration, "register");

        var tokens = await SignInAsync(client, email);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        Guid? sessionId = null;
        if (registerSession)
        {
            sessionId = await RegisterSessionAsync(client, deviceInfo);
            client.DefaultRequestHeaders.Add("X-Session-Id", sessionId.Value.ToString());
        }

        var userId = await ResolveUserIdAsync(fixture, email);

        return new TestUser(userId, email, username, DefaultPassword, tokens.AccessToken,
            tokens.RefreshToken, sessionId, client);
    }

    /// <summary>
    /// Signs the same account in again on a fresh client, as a second browser would. The returned
    /// user shares the account but has its own token and its own session.
    /// </summary>
    public static async Task<TestUser> SignInAgainAsync(
        ChatAppFixture fixture,
        TestUser user,
        string? deviceInfo = "Second integration test device",
        string? userAgent = "IntegrationTests-Second/1.0")
    {
        var client = fixture.App.CreateClient();
        if (userAgent is not null) client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        var tokens = await SignInAsync(client, user.Email, user.Password);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var sessionId = await RegisterSessionAsync(client, deviceInfo);
        client.DefaultRequestHeaders.Add("X-Session-Id", sessionId.ToString());

        return user with
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            SessionId = sessionId,
            Client = client,
        };
    }

    /// <summary>Registers an extra session on an existing signed-in client's account.</summary>
    public static async Task<Guid> RegisterSessionAsync(HttpClient client, string? deviceInfo)
    {
        var response = await client.PostAsJsonAsync("/api/Sessions/register", new { deviceInfo });
        await EnsureSuccessAsync(response, "session registration");
        var body = await response.Content.ReadFromJsonAsync<SessionRegistrationResponse>();
        return body!.Id;
    }

    private static async Task<AccessTokenResponse> SignInAsync(
        HttpClient client, string email, string password = DefaultPassword)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        await EnsureSuccessAsync(response, "login");
        var tokens = await response.Content.ReadFromJsonAsync<AccessTokenResponse>();
        if (tokens?.AccessToken is null)
            throw new InvalidOperationException($"Login for {email} returned no access token.");
        return tokens;
    }

    private static async Task<string> ResolveUserIdAsync(ChatAppFixture fixture, string email) =>
        await fixture.App.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email)
                       ?? throw new InvalidOperationException($"User {email} was not created.");
            return user.Id;
        });

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string step)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"AuthHelper {step} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    private sealed record SessionRegistrationResponse([property: JsonPropertyName("id")] Guid Id);

    private sealed record AccessTokenResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("refreshToken")] string? RefreshToken);
}

/// <summary>An authenticated account plus a client that is already carrying its credentials.</summary>
/// <param name="Client">
/// Pre-configured with <c>Authorization: Bearer</c> and, unless the user was created without one,
/// <c>X-Session-Id</c> — the same two headers <c>FE/src/api/client.ts</c> sends.
/// </param>
public sealed record TestUser(
    string UserId,
    string Email,
    string Username,
    string Password,
    string? AccessToken,
    string? RefreshToken,
    Guid? SessionId,
    HttpClient Client)
{
    /// <summary>A client for this user with no <c>X-Session-Id</c> header.</summary>
    public HttpClient WithoutSessionHeader(ChatAppFixture fixture)
    {
        var client = fixture.App.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", AccessToken);
        return client;
    }
}
