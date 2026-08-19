namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>authentication</c>, mirroring
/// <c>openspec/specs/authentication/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// The capability's client-side halves — transparent token refresh and persistent login — sit at
/// the frontend and end-to-end layers and have no constants here. See
/// <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class AuthenticationSpec
{
    private const string SignIn = "@spec:authentication/sign-in-with-email-and-password/";
    private const string Bearer = "@spec:authentication/bearer-token-authorization/";
    private const string SignOut = "@spec:authentication/sign-out-affects-only-the-current-browser/";
    private const string PasswordChange = "@spec:authentication/password-change/";
    private const string PasswordReset = "@spec:authentication/password-reset-by-email/";

    public const string ValidCredentials = SignIn + "valid-credentials ";
    public const string InvalidCredentials = SignIn + "invalid-credentials ";
    public const string RepeatedFailures = SignIn + "repeated-failures ";

    public const string MissingToken = Bearer + "missing-token ";
    public const string PublicCatalogException = Bearer + "public-catalog-exception ";

    public const string SigningOutOneBrowser = SignOut + "signing-out-one-browser ";

    public const string SuccessfulChange = PasswordChange + "successful-change ";
    public const string WrongCurrentPassword = PasswordChange + "wrong-current-password ";
    public const string WeakNewPassword = PasswordChange + "weak-new-password ";

    public const string RequestingAReset = PasswordReset + "requesting-a-reset ";
    public const string CompletingAReset = PasswordReset + "completing-a-reset ";
    public const string InvalidResetCode = PasswordReset + "invalid-reset-code ";
}
