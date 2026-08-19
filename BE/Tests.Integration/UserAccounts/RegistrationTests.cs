using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.UserAccounts;

/// <summary>
/// Backend integration tests for the registration half of
/// <c>openspec/specs/user-accounts/spec.md</c>: what makes an account, what makes a rejection, and
/// which field the rejection names.
///
/// Everything goes through <c>POST /api/auth/register</c> and <c>POST /api/auth/login</c>, the two
/// endpoints a visitor actually reaches. A rejection is asserted by the field it names rather than
/// by its wording, so rephrasing an error does not break a test.
/// </summary>
[Collection(Collections.Identity)]
public class RegistrationTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = UserAccountsSpec.SuccessfulRegistration
                        + "an unused email and username create an account that can sign in immediately")]
    public async Task Registration_creates_an_account_that_can_sign_in_at_once()
    {
        var client = App.CreateClient();
        var (email, username) = FreshIdentity();

        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            username,
            password = AuthHelper.DefaultPassword,
        });

        registration.StatusCode.Should().Be(HttpStatusCode.OK);

        var signIn = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = AuthHelper.DefaultPassword,
        });

        signIn.IsSuccessStatusCode.Should().BeTrue(
            "no email verification stands between registering and using the account");

        var tokens = await signIn.Content.ReadFromJsonAsync<JsonElement>();
        tokens.TryGetProperty("accessToken", out var accessToken).Should().BeTrue();
        accessToken.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Theory(DisplayName = UserAccountsSpec.MissingField
                          + "registration missing a required field is rejected naming that field, and creates nothing")]
    [InlineData("email")]
    [InlineData("username")]
    [InlineData("password")]
    public async Task Registration_without_a_required_field_is_rejected_naming_it(string omitted)
    {
        var client = App.CreateClient();
        var (email, username) = FreshIdentity();

        var body = new Dictionary<string, string?>
        {
            ["email"] = email,
            ["username"] = username,
            ["password"] = AuthHelper.DefaultPassword,
        };
        body[omitted] = null;

        var response = await client.PostAsJsonAsync("/api/auth/register", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ValidationFieldsAsync(response)).Should().Contain(
            field => string.Equals(field, omitted, StringComparison.OrdinalIgnoreCase),
            "the rejection is field-level, so the form can point at the field that is wrong");

        // Nothing was created: the same identity is still free.
        var retry = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            username,
            password = AuthHelper.DefaultPassword,
        });
        retry.StatusCode.Should().Be(HttpStatusCode.OK, "the rejected attempt left no account behind");
    }

    [Fact(DisplayName = UserAccountsSpec.DuplicateUsername
                        + "a username already in use is rejected, case-insensitively, naming the username")]
    public async Task Duplicate_username_is_rejected_case_insensitively()
    {
        var existing = await CreateUserAsync();
        var client = App.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"other-{Guid.NewGuid():N}@example.test",
            username = existing.Username.ToUpperInvariant(),
            password = AuthHelper.DefaultPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ValidationFieldsAsync(response)).Should().Contain("username");
    }

    [Fact(DisplayName = UserAccountsSpec.DuplicateEmail
                        + "an email already in use is rejected, case-insensitively, naming the email")]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        var existing = await CreateUserAsync();
        var client = App.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = existing.Email.ToUpperInvariant(),
            username = $"other{Guid.NewGuid():N}"[..16],
            password = AuthHelper.DefaultPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ValidationFieldsAsync(response)).Should().Contain("email");
    }

    [Theory(DisplayName = UserAccountsSpec.InvalidUsernameCharacters
                          + "a username outside 3-32 letters, digits, dot, underscore, or hyphen is rejected")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has@at")]
    [InlineData("emoji\U0001F600")]
    [InlineData("ab")]
    [InlineData("thisusernameislongerthanthirtytwocharacters")]
    public async Task Usernames_outside_the_allowed_format_are_rejected(string username)
    {
        var client = App.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"user-{Guid.NewGuid():N}@example.test",
            username,
            password = AuthHelper.DefaultPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ValidationFieldsAsync(response)).Should().Contain("username");
    }

    [Theory(DisplayName = UserAccountsSpec.UsernameEqualsEmail
                          + "a username equal to the submitted email, ignoring case, is rejected")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Username_equal_to_the_email_is_rejected(bool differentCase)
    {
        var client = App.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            username = differentCase ? email.ToUpperInvariant() : email,
            password = AuthHelper.DefaultPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ValidationFieldsAsync(response)).Should().Contain("username");
    }

    private static (string Email, string Username) FreshIdentity()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return ($"user-{suffix}@example.test", $"user{suffix}");
    }

    /// <summary>
    /// The keys of the <c>{ "errors": { field: [messages] } }</c> body a rejected registration
    /// returns. Two different producers use that shape and they disagree on casing: the
    /// <c>[ApiController]</c> model-validation filter rejects a missing <c>[Required]</c> field
    /// before the request reaches the service and names it <c>Username</c>, while the service's own
    /// <c>FieldError</c> names it <c>username</c>. Both are field-level, which is what the
    /// specification asks for, so callers compare case-insensitively.
    /// </summary>
    private static async Task<IEnumerable<string>> ValidationFieldsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (!body.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Expected a field-level validation body, got: {body.GetRawText()}");
        }

        return errors.EnumerateObject().Select(field => field.Name).ToList();
    }
}
