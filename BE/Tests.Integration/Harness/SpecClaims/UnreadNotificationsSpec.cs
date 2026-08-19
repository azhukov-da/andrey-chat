namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>unread-notifications</c>, mirroring
/// <c>openspec/specs/unread-notifications/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// Badge rendering, the aggregate count, the HTTP fallback when the hub is down, and the
/// invitations section of the sidebar are client behaviour and have no constants here. See
/// <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class UnreadNotificationsSpec
{
    private const string Indicators = "@spec:unread-notifications/unread-indicators/";
    private const string Clearing = "@spec:unread-notifications/clearing-unread-state/";
    private const string Typing = "@spec:unread-notifications/typing-indicators/";
    private const string FriendRequests = "@spec:unread-notifications/friend-request-notification/";

    public const string MessageArrivesInABackgroundChat = Indicators + "message-arrives-in-a-background-chat ";

    public const string OpeningAChatClearsTheBadge = Clearing + "opening-a-chat-clears-the-badge ";
    public const string ReadPositionRecordedPerMembership = Clearing + "read-position-recorded-per-membership ";
    public const string MarkingReadInAChatOneIsNotIn = Clearing + "marking-read-in-a-chat-one-is-not-in ";

    public const string Composing = Typing + "composing ";
    public const string SendingStopsTyping = Typing + "sending-stops-typing ";

    public const string ReceivingARequestWhileOnline = FriendRequests + "receiving-a-request-while-online ";
}
