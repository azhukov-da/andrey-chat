namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>messaging</c>, mirroring
/// <c>openspec/specs/messaging/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// The composer's byte counter, cancelling a pending reply, and a 10,000-message room staying
/// responsive are not here: the first two are client-side and the third belongs to the load layer.
/// See <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class MessagingSpec
{
    private const string Sending = "@spec:messaging/sending-messages/";
    private const string SizeLimit = "@spec:messaging/message-size-limit/";
    private const string Replies = "@spec:messaging/replies/";
    private const string Editing = "@spec:messaging/editing-messages/";
    private const string Deleting = "@spec:messaging/deleting-messages/";
    private const string History = "@spec:messaging/persistent-history-in-chronological-order/";
    private const string Paging = "@spec:messaging/incremental-history-loading/";
    private const string Access = "@spec:messaging/history-access-control/";

    public const string MemberSendsText = Sending + "member-sends-text ";
    public const string MultilineAndEmoji = Sending + "multiline-and-emoji ";
    public const string NonMemberSends = Sending + "non-member-sends ";
    public const string BannedMemberSends = Sending + "banned-member-sends ";

    public const string OversizedSend = SizeLimit + "oversized-send ";

    public const string ComposingAReply = Replies + "composing-a-reply ";
    public const string QuotingADeletedMessage = Replies + "quoting-a-deleted-message ";
    public const string QuotingAnAttachmentMessage = Replies + "quoting-an-attachment-message ";
    public const string ReplyTargetInAnotherRoom = Replies + "reply-target-in-another-room ";

    public const string AuthorEdits = Editing + "author-edits ";
    public const string NonAuthorEdits = Editing + "non-author-edits ";
    public const string EditingADeletedMessage = Editing + "editing-a-deleted-message ";

    public const string AuthorDeletes = Deleting + "author-deletes ";
    public const string AdminDeletesAnotherUsersMessage = Deleting + "admin-deletes-another-user-s-message ";
    public const string UnauthorizedDelete = Deleting + "unauthorized-delete ";
    public const string DeletingTwice = Deleting + "deleting-twice ";

    public const string ReadingHistory = History + "reading-history ";
    public const string OfflineRecipient = History + "offline-recipient ";
    public const string HistoryAcrossRestarts = History + "history-across-restarts ";

    public const string ScrollingBack = Paging + "scrolling-back ";
    public const string ReachingTheBeginning = Paging + "reaching-the-beginning ";

    public const string NonMemberRequestsHistory = Access + "non-member-requests-history ";
}
