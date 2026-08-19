namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>user-accounts</c>, mirroring
/// <c>openspec/specs/user-accounts/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
/// </summary>
public static class UserAccountsSpec
{
    private const string Registration = "@spec:user-accounts/self-registration/";
    private const string Uniqueness = "@spec:user-accounts/email-and-username-uniqueness/";
    private const string UsernameRules = "@spec:user-accounts/username-format-and-immutability/";
    private const string DisplayName = "@spec:user-accounts/display-name/";
    private const string Profile = "@spec:user-accounts/own-profile-retrieval/";
    private const string Deletion = "@spec:user-accounts/account-deletion/";

    public const string SuccessfulRegistration = Registration + "successful-registration ";
    public const string MissingField = Registration + "missing-field ";

    public const string DuplicateUsername = Uniqueness + "duplicate-username ";
    public const string DuplicateEmail = Uniqueness + "duplicate-email ";

    public const string InvalidUsernameCharacters = UsernameRules + "invalid-username-characters ";
    public const string UsernameEqualsEmail = UsernameRules + "username-equals-email ";
    public const string NoRenamePath = UsernameRules + "no-rename-path ";

    public const string SettingADisplayName = DisplayName + "setting-a-display-name ";
    public const string NoDisplayName = DisplayName + "no-display-name ";

    public const string ReadingOwnProfile = Profile + "reading-own-profile ";
    public const string UnauthenticatedProfileRequest = Profile + "unauthenticated-profile-request ";

    public const string DeletingAnAccount = Deletion + "deleting-an-account ";
    public const string DeletingTwice = Deletion + "deleting-twice ";
}
