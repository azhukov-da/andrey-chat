namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>room-moderation</c>, mirroring
/// <c>openspec/specs/room-moderation/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// How roles are labelled in the member list is rendering and has no constant here. See
/// <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class RoomModerationSpec
{
    private const string Roles = "@spec:room-moderation/room-roles/";
    private const string Authority = "@spec:room-moderation/moderation-requires-admin-authority/";
    private const string Admins = "@spec:room-moderation/managing-admins/";
    private const string Removal = "@spec:room-moderation/removal-is-a-ban/";
    private const string Banning = "@spec:room-moderation/banning-users/";
    private const string BanList = "@spec:room-moderation/ban-list-visibility-and-unbanning/";
    private const string LostAccess = "@spec:room-moderation/loss-of-room-access/";
    private const string Invitations = "@spec:room-moderation/room-invitations/";
    private const string Responding = "@spec:room-moderation/responding-to-invitations/";

    public const string OwnerPrivilegesArePermanent = Roles + "owner-privileges-are-permanent ";

    public const string MemberAttemptsModeration = Authority + "member-attempts-moderation ";
    public const string OutsiderAttemptsModeration = Authority + "outsider-attempts-moderation ";

    public const string PromotingAMember = Admins + "promoting-a-member ";
    public const string DemotingAnAdmin = Admins + "demoting-an-admin ";
    public const string PromotingAnExistingAdmin = Admins + "promoting-an-existing-admin ";
    public const string TargetIsNotAMember = Admins + "target-is-not-a-member ";

    public const string RemovingAMember = Removal + "removing-a-member ";
    public const string RemovedUserTriesToRejoin = Removal + "removed-user-tries-to-rejoin ";
    public const string AdminRemovingAnAdmin = Removal + "admin-removing-an-admin ";

    public const string BanningAMemberWithAReason = Banning + "banning-a-member-with-a-reason ";
    public const string BanningAnAlreadyBannedUser = Banning + "banning-an-already-banned-user ";
    public const string AdminBanningAnAdmin = Banning + "admin-banning-an-admin ";

    public const string ViewingBans = BanList + "viewing-bans ";
    public const string Unbanning = BanList + "unbanning ";
    public const string UnbanningSomeoneNotBanned = BanList + "unbanning-someone-not-banned ";

    public const string ReadingHistoryAfterRemoval = LostAccess + "reading-history-after-removal ";
    public const string DownloadingAnAttachmentAfterRemoval = LostAccess + "downloading-an-attachment-after-removal ";

    public const string SendingAnInvitation = Invitations + "sending-an-invitation ";
    public const string InvitingABannedUser = Invitations + "inviting-a-banned-user ";
    public const string DuplicateInvitation = Invitations + "duplicate-invitation ";
    public const string NonAdminAttemptsToInvite = Invitations + "non-admin-attempts-to-invite ";

    public const string AcceptingAnInvitation = Responding + "accepting-an-invitation ";
    public const string RejectingAnInvitation = Responding + "rejecting-an-invitation ";
    public const string AnsweringSomeoneElsesInvitation = Responding + "answering-someone-else-s-invitation ";
    public const string AnsweringTwice = Responding + "answering-twice ";
    public const string InvitationToADeletedRoom = Responding + "invitation-to-a-deleted-room ";
    public const string BannedBeforeAccepting = Responding + "banned-before-accepting ";
}
