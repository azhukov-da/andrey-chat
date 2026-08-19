namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>chat-rooms</c>, mirroring
/// <c>openspec/specs/chat-rooms/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// Three of the capability's scenarios are not here: the private rooms screen and the visibility of
/// the Leave control are rendering, and a member being navigated out of a room someone else deleted
/// needs two live browsers. See <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class ChatRoomsSpec
{
    private const string Creation = "@spec:chat-rooms/room-creation/";
    private const string Naming = "@spec:chat-rooms/room-name-uniqueness/";
    private const string Properties = "@spec:chat-rooms/room-properties/";
    private const string Catalog = "@spec:chat-rooms/public-room-catalog/";
    private const string Joining = "@spec:chat-rooms/joining-public-rooms/";
    private const string PrivateAccess = "@spec:chat-rooms/private-room-access/";
    private const string Leaving = "@spec:chat-rooms/leaving-rooms/";
    private const string Settings = "@spec:chat-rooms/room-settings-maintenance/";
    private const string Deletion = "@spec:chat-rooms/room-deletion/";

    public const string CreatingARoom = Creation + "creating-a-room ";
    public const string MissingName = Creation + "missing-name ";

    public const string DuplicateNameOnCreation = Naming + "duplicate-name-on-creation ";
    public const string DuplicateNameOnRename = Naming + "duplicate-name-on-rename ";
    public const string ReusingADeletedRoomsName = Naming + "reusing-a-deleted-room-s-name ";

    public const string ReadingARoom = Properties + "reading-a-room ";
    public const string NonMemberReadsAPublicRoom = Properties + "non-member-reads-a-public-room ";

    public const string BrowsingTheCatalog = Catalog + "browsing-the-catalog ";
    public const string Searching = Catalog + "searching ";
    public const string PrivateRoomsHidden = Catalog + "private-rooms-hidden ";
    public const string CatalogAlreadyAMember = Catalog + "already-a-member ";

    public const string Joining_ = Joining + "joining ";
    public const string BannedFromTheRoom = Joining + "banned-from-the-room ";
    public const string JoinAlreadyAMember = Joining + "already-a-member ";
    public const string PrivateRoomJoinAttempt = Joining + "private-room-join-attempt ";

    public const string NonMemberRequestsAPrivateRoom = PrivateAccess + "non-member-requests-a-private-room ";

    public const string MemberLeaves = Leaving + "member-leaves ";
    public const string OwnerAttemptsToLeave = Leaving + "owner-attempts-to-leave ";

    public const string OwnerSavesSettings = Settings + "owner-saves-settings ";
    public const string AdminAttemptsToChangeSettings = Settings + "admin-attempts-to-change-settings ";

    public const string OwnerDeletesARoom = Deletion + "owner-deletes-a-room ";
    public const string NonOwnerAttemptsDeletion = Deletion + "non-owner-attempts-deletion ";
    public const string MessagingADeletedRoom = Deletion + "messaging-a-deleted-room ";
}
