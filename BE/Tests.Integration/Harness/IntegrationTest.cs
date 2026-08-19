namespace Tests.Integration.Harness;

/// <summary>
/// Base class for backend integration tests. Truncates the database before each test, so every
/// test starts from empty and none of them depend on another's leftovers.
/// </summary>
/// <remarks>
/// Declaring a scenario claim: put <c>@spec:&lt;capability&gt;/&lt;requirement&gt;/&lt;scenario&gt;</c>
/// in the test's display name and <c>tools/spec-coverage</c> will pick it up out of the .trx.
/// <code>
/// [Fact(DisplayName = UserSessionsSpec.OnlyOwnSessions + "a user never sees another account's sessions")]
/// </code>
/// The claim has to be a <c>const string</c>, because an attribute argument must be a compile-time
/// constant — hence the per-capability constant classes rather than a helper method. Concatenating
/// two constants is itself constant, so the marker and the description compose. A test covering
/// several scenarios concatenates several markers.
/// </remarks>
[Collection(ChatAppCollection.Name)]
public abstract class IntegrationTest(ChatAppFixture fixture) : IAsyncLifetime
{
    protected ChatAppFixture Fixture { get; } = fixture;

    protected ChatAppFactory App => Fixture.App;

    public virtual async ValueTask InitializeAsync() => await Fixture.ResetAsync();

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Registers, signs in, and session-registers a fresh user.</summary>
    protected Task<TestUser> CreateUserAsync(
        string? deviceInfo = "Integration test device",
        string? userAgent = "IntegrationTests/1.0",
        bool registerSession = true) =>
        AuthHelper.CreateUserAsync(Fixture, deviceInfo, userAgent, registerSession);

    /// <summary>Connects an authenticated hub client for <paramref name="user"/>.</summary>
    protected Task<TestHubClient> ConnectHubAsync(TestUser user) =>
        TestHubClient.ConnectAsync(Fixture, user);
}

/// <summary>
/// Scenario claim markers for <c>user-sessions</c>, mirroring
/// <c>openspec/specs/user-sessions/spec.md</c>.
///
/// Each constant is the full <c>@spec:</c> marker with a trailing space, so a test's display name is
/// the constant plus a description. <c>tools/spec-coverage</c> validates every marker against the
/// specs and fails the run when one matches no scenario — so a renamed scenario surfaces here as a
/// named failure rather than as silently missing coverage.
///
/// A capability being tested for the first time gets its own class like this one.
/// </summary>
public static class UserSessionsSpec
{
    private const string Registration = "@spec:user-sessions/session-registration-per-browser/";
    private const string Listing = "@spec:user-sessions/listing-active-sessions/";
    private const string Revoking = "@spec:user-sessions/revoking-sessions/";

    public const string NewSignInRegistersASession = Registration + "new-sign-in-registers-a-session ";
    public const string RestoredLoginWithoutASession = Registration + "restored-login-without-a-session ";
    public const string RegistrationFailureIsNonFatal = Registration + "session-registration-failure-is-non-fatal ";

    public const string ViewingSessions = Listing + "viewing-sessions ";
    public const string OnlyOwnSessions = Listing + "only-own-sessions ";

    public const string RevokingAnotherSession = Revoking + "revoking-another-session ";
    public const string RevokingTheCurrentSession = Revoking + "revoking-the-current-session ";
    public const string RevokingAForeignSession = Revoking + "revoking-a-foreign-session ";
}
