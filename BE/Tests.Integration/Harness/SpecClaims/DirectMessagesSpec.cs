namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>direct-messages</c>, mirroring
/// <c>openspec/specs/direct-messages/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// Whether a direct chat offers role and management affordances is rendering, and has no constant
/// here. See <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class DirectMessagesSpec
{
    private const string TwoParticipant = "@spec:direct-messages/direct-chats-are-two-participant-chats/";
    private const string Opening = "@spec:direct-messages/opening-a-direct-chat-requires-friendship/";
    private const string Frozen = "@spec:direct-messages/frozen-direct-chats/";
    private const string Sidebar = "@spec:direct-messages/direct-chats-in-the-sidebar/";

    public const string FeatureParity = TwoParticipant + "feature-parity ";
    public const string NotInTheCatalog = TwoParticipant + "not-in-the-catalog ";

    public const string MessagingAFriendForTheFirstTime = Opening + "messaging-a-friend-for-the-first-time ";
    public const string ReopeningAnExistingChat = Opening + "reopening-an-existing-chat ";
    public const string NotFriends = Opening + "not-friends ";
    public const string BlockedInEitherDirection = Opening + "blocked-in-either-direction ";
    public const string ChatWithSelf = Opening + "chat-with-self ";

    public const string SendingInAFrozenChat = Frozen + "sending-in-a-frozen-chat ";
    public const string ReadingAFrozenChat = Frozen + "reading-a-frozen-chat ";
    public const string UnblockingRestoresMessaging = Frozen + "unblocking-restores-messaging ";

    public const string ListingDirectChats = Sidebar + "listing-direct-chats ";
}
