namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>contacts-and-blocking</c>, mirroring
/// <c>openspec/specs/contacts-and-blocking/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
/// </summary>
public static class ContactsAndBlockingSpec
{
    private const string ContactList = "@spec:contacts-and-blocking/personal-contact-list/";
    private const string Requesting = "@spec:contacts-and-blocking/sending-a-friend-request/";
    private const string Confirming = "@spec:contacts-and-blocking/confirming-friendship/";
    private const string Removing = "@spec:contacts-and-blocking/removing-a-contact/";
    private const string Blocking = "@spec:contacts-and-blocking/blocking-a-user/";
    private const string Unblocking = "@spec:contacts-and-blocking/unblocking/";

    public const string ViewingContacts = ContactList + "viewing-contacts ";
    public const string OutgoingRequestsAreNotListedAsContacts =
        ContactList + "outgoing-requests-are-not-listed-as-contacts ";

    public const string RequestByUsername = Requesting + "request-by-username ";
    public const string RequestWithAMessage = Requesting + "request-with-a-message ";
    public const string UnknownTarget = Requesting + "unknown-target ";
    public const string DuplicateRequest = Requesting + "duplicate-request ";
    public const string SelfRequest = Requesting + "self-request ";

    public const string Accepting = Confirming + "accepting ";
    public const string Rejecting = Confirming + "rejecting ";
    public const string SenderCannotSelfAccept = Confirming + "sender-cannot-self-accept ";

    public const string RemovingAFriend = Removing + "removing-a-friend ";

    public const string BlockingFromTheContactList = Blocking + "blocking-from-the-contact-list ";
    public const string BlockedUserAttemptsToWrite = Blocking + "blocked-user-attempts-to-write ";
    public const string HistoryRemainsReadable = Blocking + "history-remains-readable ";
    public const string BlockingOneself = Blocking + "blocking-oneself ";

    public const string Unblocking_ = Unblocking + "unblocking ";
    public const string UnblockingSomeoneWhoIsNotBlocked = Unblocking + "unblocking-someone-who-is-not-blocked ";
}
