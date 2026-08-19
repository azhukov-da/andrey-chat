using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Common;
using Application.Features.Rooms.Dtos;
using Domain.Entities;
using Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.Authentication;

/// <summary>
/// Backend integration tests for the server-side half of
/// <c>openspec/specs/authentication/spec.md</c>: what credentials buy, what a missing token costs,
/// and which single endpoint is exempt.
///
/// The capability's other half — detecting a 401 and refreshing, and whether credentials outlive a
/// browser session — is client behaviour and is verified at the frontend and end-to-end layers.
/// </summary>
[Collection(Collections.Identity)]
public class SignInTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = AuthenticationSpec.ValidCredentials
                        + "a correct email and password return an access token, a refresh token, and a lifetime")]
    public async Task Correct_credentials_return_a_full_token_set()
    {
        var user = await CreateUserAsync();
        var client = App.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = user.Email, password = user.Password });

        response.IsSuccessStatusCode.Should().BeTrue();
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();

        tokens.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        tokens.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        tokens.GetProperty("expiresIn").GetInt64().Should().BePositive(
            "the client needs the access token's lifetime to know when to refresh");

        // And the token works: the client can load the profile with it, which is what the scenario
        // says happens next.
        var authenticated = App.CreateClient();
        authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());
        (await authenticated.GetAsync("/api/Me")).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact(DisplayName = AuthenticationSpec.ValidCredentials
                        + "a username may be used in place of the email address")]
    public async Task The_username_is_accepted_where_the_email_is()
    {
        var user = await CreateUserAsync();
        var client = App.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = user.Username, password = user.Password });

        response.IsSuccessStatusCode.Should().BeTrue(
            "the specification authenticates from an email or a username");
    }

    [Theory(DisplayName = AuthenticationSpec.InvalidCredentials
                          + "an unknown email or a wrong password fails as unauthorized and issues no tokens")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bad_credentials_are_unauthorized_and_issue_nothing(bool unknownAccount)
    {
        var user = await CreateUserAsync();
        var client = App.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = unknownAccount ? $"nobody-{Guid.NewGuid():N}@example.test" : user.Email,
            password = unknownAccount ? user.Password : "WrongPassword1!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("accessToken").And.NotContain("refreshToken");
    }

    [Fact(DisplayName = AuthenticationSpec.RepeatedFailures
                        + "repeated wrong passwords count toward lockout and eventually lock the account")]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        var user = await CreateUserAsync();
        var client = App.CreateClient();

        // The configured allowance is five failures; the sixth attempt is the one that must be
        // refused even though the password is right.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failure = await client.PostAsJsonAsync(
                "/api/auth/login", new { email = user.Email, password = "WrongPassword1!" });
            failure.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var failedCount = await App.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = await users.FindByEmailAsync(user.Email);
            return await users.GetAccessFailedCountAsync(stored!) + (await users.IsLockedOutAsync(stored!) ? 100 : 0);
        });

        failedCount.Should().BeGreaterThan(0,
            "failures have to be counted somewhere for lockout to be reachable at all");

        var withTheRightPassword = await client.PostAsJsonAsync(
            "/api/auth/login", new { email = user.Email, password = user.Password });

        withTheRightPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the account is locked out, so even the correct password is refused");
    }

    [Theory(DisplayName = AuthenticationSpec.MissingToken
                          + "a protected endpoint called without an access token is rejected with 401")]
    [InlineData("/api/Me")]
    [InlineData("/api/Rooms/mine")]
    [InlineData("/api/Friends")]
    [InlineData("/api/Sessions")]
    [InlineData("/api/me/invitations")]
    public async Task Protected_endpoints_without_a_token_return_401(string path)
    {
        var anonymous = App.CreateClient();

        var response = await anonymous.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = AuthenticationSpec.MissingToken
                        + "an invalid access token is rejected the same way a missing one is")]
    public async Task An_invalid_token_is_rejected_as_unauthorized()
    {
        var client = App.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var response = await client.GetAsync("/api/Me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = AuthenticationSpec.PublicCatalogException
                        + "the public room catalog answers an unauthenticated caller, with no membership fields set")]
    public async Task The_public_catalog_answers_without_a_token()
    {
        var owner = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Public);

        var anonymous = App.CreateClient();
        var response = await anonymous.GetAsync("/api/Rooms/public");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the catalog is the one operation a visitor may reach without signing in");

        var catalog = await response.Content.ReadFromJsonAsync<Paged<RoomDto>>();
        var listed = catalog!.Items.Single(r => r.Id == room.Id);

        listed.MyRole.Should().BeNull(
            "there is no caller to have a role, so no membership-specific field may be populated");
    }
}
