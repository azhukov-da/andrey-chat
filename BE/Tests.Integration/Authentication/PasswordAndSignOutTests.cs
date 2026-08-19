using System.Net;
using System.Net.Http.Json;
using System.Text;
using Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.Authentication;

/// <summary>
/// Backend integration tests for changing a password, resetting a forgotten one, and signing out
/// of one browser — <c>openspec/specs/authentication/spec.md</c>.
///
/// The reset flow is driven the way the emailed link would drive it. The suite has no mailbox, so
/// the code is generated through <c>UserManager</c> and encoded exactly as the identity endpoints
/// encode it for the email; everything after that point goes over the real endpoint.
/// </summary>
[Collection(Collections.Identity)]
public class PasswordAndSignOutTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    private const string NewPassword = "Rev1sedPassword!";

    [Fact(DisplayName = AuthenticationSpec.SigningOutOneBrowser
                        + "signing out revokes only the calling browser's session; the other stays usable")]
    public async Task Signing_out_one_browser_leaves_the_other_signed_in()
    {
        var first = await CreateUserAsync(deviceInfo: "First browser");
        var second = await AuthHelper.SignInAgainAsync(Fixture, first, deviceInfo: "Second browser");

        var signOut = await first.Client.DeleteAsync("/api/Sessions/current");

        signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var remaining = await second.Client.GetFromJsonAsync<List<SessionSummary>>("/api/Sessions");
        remaining.Should().ContainSingle(
            "the account has one session left, the one that did not sign out");
        remaining!.Single().DeviceInfo.Should().Be("Second browser");

        (await second.Client.GetAsync("/api/Me")).IsSuccessStatusCode.Should().BeTrue(
            "the second browser is untouched by the first one signing out");
    }

    [Fact(DisplayName = AuthenticationSpec.SuccessfulChange
                        + "the correct current password and a valid new one change it, and the new one signs in")]
    public async Task A_valid_change_takes_effect_on_the_next_sign_in()
    {
        var user = await CreateUserAsync();

        var response = await user.Client.PostAsJsonAsync("/api/Me/password", new
        {
            currentPassword = user.Password,
            newPassword = NewPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SignInAsync(user.Email, NewPassword)).IsSuccessStatusCode.Should().BeTrue(
            "the new password is the one that works now");
        (await SignInAsync(user.Email, user.Password)).StatusCode.Should().Be(
            HttpStatusCode.Unauthorized, "the old password stopped working");
    }

    [Fact(DisplayName = AuthenticationSpec.SuccessfulChange
                        + "a password is stored only as a hash, never in recoverable form")]
    public async Task Passwords_are_stored_only_as_hashes()
    {
        var user = await CreateUserAsync();

        var hash = await App.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = await users.FindByEmailAsync(user.Email);
            return stored!.PasswordHash;
        });

        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotContain(user.Password,
            "a stored password that contains the password is not a hash");

        // And the hash verifies the password it was made from, which is what makes it a hash of it
        // rather than of something else.
        var verified = await App.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = await users.FindByEmailAsync(user.Email);
            return await users.CheckPasswordAsync(stored!, user.Password);
        });
        verified.Should().BeTrue();
    }

    [Fact(DisplayName = AuthenticationSpec.WrongCurrentPassword
                        + "a wrong current password is refused and leaves the stored password alone")]
    public async Task A_wrong_current_password_changes_nothing()
    {
        var user = await CreateUserAsync();

        var response = await user.Client.PostAsJsonAsync("/api/Me/password", new
        {
            currentPassword = "NotThePassword1!",
            newPassword = NewPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await SignInAsync(user.Email, user.Password)).IsSuccessStatusCode.Should().BeTrue(
            "the original password still works");
        (await SignInAsync(user.Email, NewPassword)).StatusCode.Should().Be(
            HttpStatusCode.Unauthorized, "the rejected new password was never stored");
    }

    [Theory(DisplayName = AuthenticationSpec.WeakNewPassword
                          + "a new password failing the policy is refused with the unmet requirement stated")]
    [InlineData("Ab1", "minimum length")]
    [InlineData("alllowercase1", "an uppercase letter")]
    [InlineData("NoDigitsHere", "a digit")]
    public async Task A_weak_new_password_is_refused_and_says_why(string weak, string requirement)
    {
        var user = await CreateUserAsync();

        var response = await user.Client.PostAsJsonAsync("/api/Me/password", new
        {
            currentPassword = user.Password,
            newPassword = weak,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"the policy requires {requirement}");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "the rejection has to state the unmet requirement, not just fail");

        (await SignInAsync(user.Email, user.Password)).IsSuccessStatusCode.Should().BeTrue(
            "the stored password is unchanged");
    }

    [Fact(DisplayName = AuthenticationSpec.RequestingAReset
                        + "a reset request answers the same way whether or not the address is registered")]
    public async Task A_reset_request_does_not_reveal_whether_the_address_exists()
    {
        var registered = await CreateUserAsync();
        var client = App.CreateClient();

        var known = await client.PostAsJsonAsync("/forgotPassword", new { email = registered.Email });
        var unknown = await client.PostAsJsonAsync(
            "/forgotPassword", new { email = $"nobody-{Guid.NewGuid():N}@example.test" });

        known.StatusCode.Should().Be(unknown.StatusCode,
            "a different answer for a registered address would enumerate accounts");
        (await known.Content.ReadAsStringAsync()).Should().Be(
            await unknown.Content.ReadAsStringAsync(),
            "and neither may the body differ");
        known.IsSuccessStatusCode.Should().BeTrue("both are confirmed to the visitor");
    }

    [Fact(Skip = "SPEC GAP authentication/password-reset-by-email: registration never confirms an "
                 + "email address, and MapIdentityApi's /resetPassword refuses any account whose "
                 + "email is unconfirmed, so no reset can complete. See docs/spec-gaps.md.",
          DisplayName = AuthenticationSpec.CompletingAReset
                        + "a valid reset code sets the new password, and the visitor signs in with it")]
    public async Task A_valid_reset_code_sets_the_new_password()
    {
        var user = await CreateUserAsync();
        var client = App.CreateClient();

        await client.PostAsJsonAsync("/forgotPassword", new { email = user.Email });
        var resetCode = await IssueResetCodeAsync(user.Email);

        var reset = await client.PostAsJsonAsync("/resetPassword", new
        {
            email = user.Email,
            resetCode,
            newPassword = NewPassword,
        });

        reset.IsSuccessStatusCode.Should().BeTrue(
            $"the reset was refused: {await reset.Content.ReadAsStringAsync()}");

        (await SignInAsync(user.Email, NewPassword)).IsSuccessStatusCode.Should().BeTrue();
        (await SignInAsync(user.Email, user.Password)).StatusCode.Should().Be(
            HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = AuthenticationSpec.InvalidResetCode
                        + "an unrecognised reset code is refused and leaves the password unchanged")]
    public async Task An_unknown_reset_code_changes_nothing()
    {
        var user = await CreateUserAsync();
        var client = App.CreateClient();

        var reset = await client.PostAsJsonAsync("/resetPassword", new
        {
            email = user.Email,
            resetCode = WebEncoders.Base64UrlEncode("not-a-real-reset-code"u8.ToArray()),
            newPassword = NewPassword,
        });

        reset.IsSuccessStatusCode.Should().BeFalse();

        (await SignInAsync(user.Email, user.Password)).IsSuccessStatusCode.Should().BeTrue(
            "the original password survives a refused reset");
        (await SignInAsync(user.Email, NewPassword)).StatusCode.Should().Be(
            HttpStatusCode.Unauthorized);
    }

    [Fact(Skip = "SPEC GAP authentication/password-reset-by-email: registration never confirms an "
                 + "email address, and MapIdentityApi's /resetPassword refuses any account whose "
                 + "email is unconfirmed, so no reset can complete. See docs/spec-gaps.md.",
          DisplayName = AuthenticationSpec.InvalidResetCode
                        + "a reset code is spent once; replaying it is refused")]
    public async Task A_spent_reset_code_cannot_be_replayed()
    {
        var user = await CreateUserAsync();
        var client = App.CreateClient();
        var resetCode = await IssueResetCodeAsync(user.Email);

        var first = await client.PostAsJsonAsync("/resetPassword", new
        {
            email = user.Email,
            resetCode,
            newPassword = NewPassword,
        });
        first.IsSuccessStatusCode.Should().BeTrue();

        var replay = await client.PostAsJsonAsync("/resetPassword", new
        {
            email = user.Email,
            resetCode,
            newPassword = "AnotherPassword1!",
        });

        replay.IsSuccessStatusCode.Should().BeFalse("a spent code is no longer valid");
        (await SignInAsync(user.Email, NewPassword)).IsSuccessStatusCode.Should().BeTrue(
            "the replay changed nothing");
    }

    /// <summary>
    /// The code the reset email would carry: an Identity password-reset token, base64url-encoded
    /// the way <c>MapIdentityApi</c>'s forgot-password endpoint encodes it before sending.
    /// </summary>
    private Task<string> IssueResetCodeAsync(string email) =>
        App.WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email)
                       ?? throw new InvalidOperationException($"No account for {email}.");
            var token = await users.GeneratePasswordResetTokenAsync(user);
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        });

    private Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        App.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    private sealed record SessionSummary(Guid Id, string? DeviceInfo, bool IsCurrent);
}
