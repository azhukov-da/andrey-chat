using System.Net;
using System.Net.Http.Json;
using Application.Features.Rooms.Dtos;
using Domain.Enums;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.RoomModeration;

/// <summary>
/// Backend integration tests for who may moderate a room and what moderating does —
/// <c>openspec/specs/room-moderation/spec.md</c>. Authority is re-evaluated on every call, so each
/// refusal here is a rule no client can talk its way around.
/// </summary>
[Collection(Collections.Social)]
public class RolesAndBansTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = RoomModerationSpec.OwnerPrivilegesArePermanent
                        + "the owner cannot be demoted, banned, or removed, by an admin or by themselves")]
    public async Task The_owner_cannot_be_demoted_banned_or_removed()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(admin);
        await owner.MakeAdminAsync(room.Id, admin);

        foreach (var actor in new[] { admin, owner })
        {
            (await actor.Client.PostAsync(
                $"/api/Rooms/{room.Id}/members/{owner.UserId}/remove-admin", content: null))
                .IsSuccessStatusCode.Should().BeFalse("the owner's authority cannot be revoked");

            (await actor.Client.PostAsJsonAsync(
                $"/api/Rooms/{room.Id}/bans/{owner.UserId}", new { reason = "trying it on" }))
                .IsSuccessStatusCode.Should().BeFalse("the owner cannot be banned from their own room");

            (await actor.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{owner.UserId}"))
                .IsSuccessStatusCode.Should().BeFalse("nor removed from it");
        }

        var members = await ListMembersAsync(owner, room.Id);
        members.Single(m => m.UserId == owner.UserId).Role.Should().Be(RoomRole.Owner,
            "after all of that, the owner is exactly as they were");
    }

    [Fact(DisplayName = RoomModerationSpec.MemberAttemptsModeration
                        + "a plain member's moderation calls are refused as not-owner-or-admin")]
    public async Task A_plain_member_may_not_moderate()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var victim = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member, victim);

        foreach (var (description, call) in ModerationCalls(member, room.Id, victim))
        {
            var response = await call();
            response.IsSuccessStatusCode.Should().BeFalse($"a member may not {description}");
            (await response.ReadErrorAsync())?.Code.Should().Be("Room.NotOwnerOrAdmin");
        }
    }

    [Fact(DisplayName = RoomModerationSpec.OutsiderAttemptsModeration
                        + "a non-member's moderation calls are refused")]
    public async Task An_outsider_may_not_moderate()
    {
        var owner = await CreateUserAsync();
        var victim = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(victim);

        foreach (var (description, call) in ModerationCalls(outsider, room.Id, victim))
        {
            var response = await call();
            response.IsSuccessStatusCode.Should().BeFalse($"an outsider may not {description}");
        }

        (await ListMembersAsync(owner, room.Id)).Should().HaveCount(2, "nothing changed");
    }

    [Fact(DisplayName = RoomModerationSpec.PromotingAMember
                        + "the owner promotes a member and the member list shows the new role")]
    public async Task Promoting_a_member_makes_them_an_admin()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);

        var response = await owner.Client.PostAsync(
            $"/api/Rooms/{room.Id}/members/{member.UserId}/make-admin", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListMembersAsync(owner, room.Id)).Single(m => m.UserId == member.UserId)
            .Role.Should().Be(RoomRole.Admin);

        // And the promotion is authority, not just a label: the new admin can moderate.
        var newcomer = await CreateUserAsync();
        await newcomer.JoinRoomAsync(room.Id);
        (await member.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{newcomer.UserId}"))
            .IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact(DisplayName = RoomModerationSpec.DemotingAnAdmin
                        + "revoking admin status returns the user to Member")]
    public async Task Demoting_an_admin_returns_them_to_member()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(admin);
        await owner.MakeAdminAsync(room.Id, admin);

        var response = await owner.Client.PostAsync(
            $"/api/Rooms/{room.Id}/members/{admin.UserId}/remove-admin", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListMembersAsync(owner, room.Id)).Single(m => m.UserId == admin.UserId)
            .Role.Should().Be(RoomRole.Member);

        // And the authority went with the role.
        var newcomer = await CreateUserAsync();
        await newcomer.JoinRoomAsync(room.Id);
        (await admin.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{newcomer.UserId}"))
            .IsSuccessStatusCode.Should().BeFalse("a demoted admin moderates nothing");
    }

    [Fact(DisplayName = RoomModerationSpec.PromotingAnExistingAdmin
                        + "promoting someone who is already an admin succeeds and changes nothing")]
    public async Task Promoting_an_existing_admin_is_a_no_op_success()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(admin);
        await owner.MakeAdminAsync(room.Id, admin);

        var again = await owner.Client.PostAsync(
            $"/api/Rooms/{room.Id}/members/{admin.UserId}/make-admin", content: null);

        again.IsSuccessStatusCode.Should().BeTrue("repeating a promotion is not an error");
        (await ListMembersAsync(owner, room.Id)).Single(m => m.UserId == admin.UserId)
            .Role.Should().Be(RoomRole.Admin);
    }

    [Fact(DisplayName = RoomModerationSpec.TargetIsNotAMember
                        + "a role change aimed at a non-member is refused as not-a-member")]
    public async Task A_role_change_needs_a_member_to_aim_at()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();

        var promote = await owner.Client.PostAsync(
            $"/api/Rooms/{room.Id}/members/{outsider.UserId}/make-admin", content: null);
        promote.IsSuccessStatusCode.Should().BeFalse();
        (await promote.ReadErrorAsync())?.Code.Should().Be("Room.NotMember");

        var demote = await owner.Client.PostAsync(
            $"/api/Rooms/{room.Id}/members/{outsider.UserId}/remove-admin", content: null);
        demote.IsSuccessStatusCode.Should().BeFalse();
        (await demote.ReadErrorAsync())?.Code.Should().Be("Room.NotMember");
    }

    [Fact(DisplayName = RoomModerationSpec.RemovingAMember
                        + "removal drops the membership and records a ban naming the acting admin")]
    public async Task Removal_drops_the_membership_and_records_a_ban()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(admin, member);
        await owner.MakeAdminAsync(room.Id, admin);

        var response = await admin.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{member.UserId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListMembersAsync(owner, room.Id)).Select(m => m.UserId)
            .Should().NotContain(member.UserId);

        var bans = await ListBansAsync(owner, room.Id);
        var ban = bans.Single(b => b.BannedUserId == member.UserId);
        ban.BannedByUserId.Should().Be(admin.UserId, "a removal names who did it");
        ban.BannedByUserName.Should().Be(admin.Username);
    }

    [Fact(DisplayName = RoomModerationSpec.RemovedUserTriesToRejoin
                        + "the removed user's attempt to rejoin the public room is refused as banned")]
    public async Task A_removed_user_cannot_rejoin()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);
        await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{member.UserId}");

        var rejoin = await member.Client.PostAsync($"/api/Rooms/{room.Id}/join", content: null);

        rejoin.IsSuccessStatusCode.Should().BeFalse();
        (await rejoin.ReadErrorAsync())?.Code.Should().Be("Room.Banned",
            "removal is a ban, which is exactly what stops the door revolving");
    }

    [Fact(DisplayName = RoomModerationSpec.AdminRemovingAnAdmin
                        + "a non-owner admin removing another admin is refused as forbidden")]
    public async Task An_admin_may_not_remove_another_admin()
    {
        var owner = await CreateUserAsync();
        var first = await CreateUserAsync();
        var second = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(first, second);
        await owner.MakeAdminAsync(room.Id, first);
        await owner.MakeAdminAsync(room.Id, second);

        var byPeer = await first.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{second.UserId}");

        byPeer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ListMembersAsync(owner, room.Id)).Select(m => m.UserId).Should().Contain(second.UserId);

        var byOwner = await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{second.UserId}");
        byOwner.IsSuccessStatusCode.Should().BeTrue("only the owner may remove an admin");
    }

    [Fact(DisplayName = RoomModerationSpec.BanningAMemberWithAReason
                        + "a ban removes the member and records who, why, and when")]
    public async Task A_ban_removes_the_member_and_records_the_details()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);
        var before = DateTime.UtcNow.AddSeconds(-5);

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{member.UserId}", new { reason = "repeatedly off topic" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListMembersAsync(owner, room.Id)).Select(m => m.UserId)
            .Should().NotContain(member.UserId, "banning removes any existing membership");

        var ban = (await ListBansAsync(owner, room.Id)).Single(b => b.BannedUserId == member.UserId);
        ban.BannedUserName.Should().Be(member.Username);
        ban.BannedByUserId.Should().Be(owner.UserId);
        ban.Reason.Should().Be("repeatedly off topic");
        ban.CreatedAt.Should().BeAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact(DisplayName = RoomModerationSpec.BanningAMemberWithAReason
                        + "a user who was never a member can be banned in advance")]
    public async Task Someone_who_is_not_a_member_can_still_be_banned()
    {
        var owner = await CreateUserAsync();
        var stranger = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{stranger.UserId}", new { reason = (string?)null });

        response.IsSuccessStatusCode.Should().BeTrue(
            "a ban is about who may be here, not only about who is here");
        (await stranger.Client.PostAsync($"/api/Rooms/{room.Id}/join", content: null))
            .IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact(DisplayName = RoomModerationSpec.BanningAnAlreadyBannedUser
                        + "banning an already banned user succeeds without a duplicate entry")]
    public async Task Banning_twice_leaves_one_entry()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);
        await owner.BanAsync(room.Id, member, "the first time");

        var again = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{member.UserId}", new { reason = "the second time" });

        again.IsSuccessStatusCode.Should().BeTrue();

        var bans = await ListBansAsync(owner, room.Id);
        bans.Where(b => b.BannedUserId == member.UserId).Should().ContainSingle(
            "a repeated ban is not a second ban");
        bans.Single(b => b.BannedUserId == member.UserId).Reason.Should().Be("the first time",
            "the original entry is left as it was");
    }

    [Fact(DisplayName = RoomModerationSpec.AdminBanningAnAdmin
                        + "a non-owner admin banning another admin is refused as forbidden")]
    public async Task An_admin_may_not_ban_another_admin()
    {
        var owner = await CreateUserAsync();
        var first = await CreateUserAsync();
        var second = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(first, second);
        await owner.MakeAdminAsync(room.Id, first);
        await owner.MakeAdminAsync(room.Id, second);

        var byPeer = await first.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{second.UserId}", new { reason = "peer trouble" });

        byPeer.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ListBansAsync(owner, room.Id)).Should().BeEmpty();

        var byOwner = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{second.UserId}", new { reason = "owner's call" });
        byOwner.IsSuccessStatusCode.Should().BeTrue("only the owner may ban an admin");
    }

    [Fact(DisplayName = RoomModerationSpec.ViewingBans
                        + "an admin reads the ban list with user, banner, timestamp, and reason; a member cannot")]
    public async Task The_ban_list_is_visible_to_admins_only()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var member = await CreateUserAsync();
        var banned = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(admin, member, banned);
        await owner.MakeAdminAsync(room.Id, admin);
        await owner.BanAsync(room.Id, banned, "spam");

        var asAdmin = await ListBansAsync(admin, room.Id);
        var entry = asAdmin.Single();
        entry.BannedUserId.Should().Be(banned.UserId);
        entry.BannedUserName.Should().Be(banned.Username);
        entry.BannedByUserName.Should().Be(owner.Username);
        entry.Reason.Should().Be("spam");
        entry.CreatedAt.Should().NotBe(default);

        var asMember = await member.Client.GetAsync($"/api/Rooms/{room.Id}/bans");
        asMember.IsSuccessStatusCode.Should().BeFalse("the ban list is a moderation view");
    }

    [Fact(DisplayName = RoomModerationSpec.Unbanning
                        + "unbanning removes the entry and lets the user join the public room again")]
    public async Task Unbanning_lets_the_user_back_in()
    {
        var owner = await CreateUserAsync();
        var banned = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(banned);
        await owner.BanAsync(room.Id, banned, "a misunderstanding");

        var response = await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}/bans/{banned.UserId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListBansAsync(owner, room.Id)).Should().BeEmpty();

        (await banned.Client.PostAsync($"/api/Rooms/{room.Id}/join", content: null))
            .IsSuccessStatusCode.Should().BeTrue("the door is open again");
    }

    [Fact(DisplayName = RoomModerationSpec.UnbanningSomeoneNotBanned
                        + "unbanning a user with no ban entry succeeds and changes nothing")]
    public async Task Unbanning_someone_who_is_not_banned_succeeds_quietly()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);

        var response = await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}/bans/{member.UserId}");

        response.IsSuccessStatusCode.Should().BeTrue();
        (await ListBansAsync(owner, room.Id)).Should().BeEmpty();
        (await ListMembersAsync(owner, room.Id)).Select(m => m.UserId).Should().Contain(member.UserId,
            "an unban that had nothing to undo also had nothing else to touch");
    }

    [Fact(DisplayName = RoomModerationSpec.ReadingHistoryAfterRemoval
                        + "a removed user's history request is refused as not-a-member")]
    public async Task A_removed_user_loses_the_history()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);
        await member.SendMessageAsync(room.Id, "written while still a member");

        (await member.GetHistoryAsync(room.Id)).Items.Should().NotBeEmpty("readable before removal");

        await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{member.UserId}");

        var response = await member.Client.GetAsync($"/api/rooms/{room.Id}/Messages");
        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NotMember");
    }

    [Fact(DisplayName = RoomModerationSpec.DownloadingAnAttachmentAfterRemoval
                        + "a removed user cannot download the room's attachments, including their own upload")]
    public async Task A_removed_user_loses_the_attachments_including_their_own()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);

        var mine = await member.UploadAsync(room.Id, "mine.txt", "text/plain", "my file"u8.ToArray());
        var theirs = await owner.UploadAsync(room.Id, "theirs.txt", "text/plain", "their file"u8.ToArray());

        var myAttachment = mine.Attachments.Single().Id;
        var theirAttachment = theirs.Attachments.Single().Id;

        (await member.Client.GetAsync($"/api/attachments/{myAttachment}"))
            .IsSuccessStatusCode.Should().BeTrue("downloadable while a member");

        await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{member.UserId}");

        (await member.Client.GetAsync($"/api/attachments/{myAttachment}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "having uploaded it does not survive losing access to the room it lives in");
        (await member.Client.GetAsync($"/api/attachments/{theirAttachment}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Every moderation operation, so an authority test can sweep all of them.</summary>
    private static IEnumerable<(string Description, Func<Task<HttpResponseMessage>> Call)>
        ModerationCalls(TestUser caller, Guid roomId, TestUser target)
    {
        yield return ("promote", () => caller.Client.PostAsync(
            $"/api/Rooms/{roomId}/members/{target.UserId}/make-admin", content: null));
        yield return ("demote", () => caller.Client.PostAsync(
            $"/api/Rooms/{roomId}/members/{target.UserId}/remove-admin", content: null));
        yield return ("remove a member", () => caller.Client.DeleteAsync(
            $"/api/Rooms/{roomId}/members/{target.UserId}"));
        yield return ("ban", () => caller.Client.PostAsJsonAsync(
            $"/api/Rooms/{roomId}/bans/{target.UserId}", new { reason = (string?)null }));
        yield return ("unban", () => caller.Client.DeleteAsync(
            $"/api/Rooms/{roomId}/bans/{target.UserId}"));
        yield return ("read the ban list", () => caller.Client.GetAsync($"/api/Rooms/{roomId}/bans"));
        yield return ("invite", () => caller.Client.PostAsJsonAsync(
            $"/api/Rooms/{roomId}/invitations", new { inviteeUsername = target.Username }));
    }

    private static async Task<List<RoomMemberDto>> ListMembersAsync(TestUser user, Guid roomId) =>
        (await user.Client.GetFromJsonAsync<List<RoomMemberDto>>($"/api/Rooms/{roomId}/members"))!;

    private static async Task<List<RoomBanDto>> ListBansAsync(TestUser user, Guid roomId) =>
        (await user.Client.GetFromJsonAsync<List<RoomBanDto>>($"/api/Rooms/{roomId}/bans"))!;
}
