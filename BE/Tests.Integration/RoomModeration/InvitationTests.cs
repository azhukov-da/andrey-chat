using System.Net;
using System.Net.Http.Json;
using Application.Features.Rooms.Dtos;
using Domain.Enums;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.RoomModeration;

/// <summary>
/// Backend integration tests for room invitations and the answers to them —
/// <c>openspec/specs/room-moderation/spec.md</c>. An invitation is the only way into a private
/// room, so who may send one and who may answer it are both access-control rules.
/// </summary>
[Collection(Collections.Social)]
public class InvitationTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = RoomModerationSpec.SendingAnInvitation
                        + "an admin's invitation is created pending and names the room, invitee, and inviter")]
    public async Task An_admin_can_invite_by_username()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(admin);
        await owner.MakeAdminAsync(room.Id, admin);

        var invitation = await admin.InviteAsync(room.Id, invitee);

        invitation.RoomId.Should().Be(room.Id);
        invitation.RoomName.Should().Be(room.Name);
        invitation.InvitedUserId.Should().Be(invitee.UserId);
        invitation.InvitedUserName.Should().Be(invitee.Username);
        invitation.InvitedByUserId.Should().Be(admin.UserId);
        invitation.Status.Should().Be(InvitationStatus.Pending);

        var theirs = await ListInvitationsAsync(invitee);
        theirs.Should().ContainSingle(i => i.Id == invitation.Id,
            "the invitee needs to see it in order to answer it");
        theirs.Single().InvitedByUserName.Should().Be(admin.Username,
            "the invitation names who sent it");
    }

    [Fact(DisplayName = RoomModerationSpec.NonAdminAttemptsToInvite
                        + "a plain member cannot invite anyone")]
    public async Task A_plain_member_cannot_invite()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);

        var response = await member.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/invitations", new { inviteeUsername = invitee.Username });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NotOwnerOrAdmin");
        (await ListInvitationsAsync(invitee)).Should().BeEmpty();
    }

    [Fact(DisplayName = RoomModerationSpec.InvitingABannedUser
                        + "inviting a user banned from the room is refused as banned")]
    public async Task A_banned_user_cannot_be_invited()
    {
        var owner = await CreateUserAsync();
        var banned = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(banned);
        await owner.BanAsync(room.Id, banned, "banned first");

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/invitations", new { inviteeUsername = banned.Username });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.Banned");
        (await ListInvitationsAsync(banned)).Should().BeEmpty();
    }

    [Fact(DisplayName = RoomModerationSpec.DuplicateInvitation
                        + "a second pending invitation for the same user and room is refused")]
    public async Task A_duplicate_pending_invitation_is_refused()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();
        await owner.InviteAsync(room.Id, invitee);

        var again = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/invitations", new { inviteeUsername = invitee.Username });

        again.IsSuccessStatusCode.Should().BeFalse();
        (await again.ReadErrorAsync())?.Code.Should().Be("Invitation.AlreadyExists");
        (await ListInvitationsAsync(invitee)).Should().ContainSingle();
    }

    [Fact(DisplayName = RoomModerationSpec.SendingAnInvitation
                        + "an invitation naming an unknown user, an existing member, or the inviter is refused")]
    public async Task An_invitation_needs_a_plausible_target()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);

        var unknown = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/invitations",
            new { inviteeUsername = $"nobody{Guid.NewGuid():N}"[..20] });
        unknown.IsSuccessStatusCode.Should().BeFalse();
        (await unknown.ReadErrorAsync())?.Code.Should().Be("User.NotFoundByUsername");

        var self = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/invitations", new { inviteeUsername = owner.Username });
        self.IsSuccessStatusCode.Should().BeFalse();
        (await self.ReadErrorAsync())?.Code.Should().Be("Invitation.CannotInviteSelf");

        var existing = await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/invitations", new { inviteeUsername = member.Username });
        existing.IsSuccessStatusCode.Should().BeFalse();
        (await existing.ReadErrorAsync())?.Code.Should().Be("Room.AlreadyMember");
    }

    [Fact(DisplayName = RoomModerationSpec.AcceptingAnInvitation
                        + "accepting makes the invitee a Member and puts the room in their list")]
    public async Task Accepting_an_invitation_grants_membership()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);
        var invitation = await owner.InviteAsync(room.Id, invitee);

        var response = await invitee.Client.PostAsync(
            $"/api/invitations/{invitation.Id}/accept", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var read = await invitee.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{room.Id}");
        read!.MyRole.Should().Be(RoomRole.Member,
            "an invitation is the way into a private room, and it lands you as a plain member");

        var mine = await invitee.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine!.Select(r => r.Id).Should().Contain(room.Id);

        (await ListInvitationsAsync(invitee)).Should().BeEmpty(
            "an answered invitation is no longer pending");

        // Membership is in force at once: posting is member-only and works on the next call.
        (await invitee.SendMessageAsync(room.Id, "thanks for the invitation")).Id.Should().NotBeEmpty();
    }

    [Fact(DisplayName = RoomModerationSpec.RejectingAnInvitation
                        + "rejecting marks it rejected and creates no membership")]
    public async Task Rejecting_an_invitation_creates_nothing()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);
        var invitation = await owner.InviteAsync(room.Id, invitee);

        var response = await invitee.Client.PostAsync(
            $"/api/invitations/{invitation.Id}/reject", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListInvitationsAsync(invitee)).Should().BeEmpty();
        (await invitee.Client.GetAsync($"/api/Rooms/{room.Id}")).IsSuccessStatusCode
            .Should().BeFalse("rejecting leaves them outside a private room");

        var mine = await invitee.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine!.Select(r => r.Id).Should().NotContain(room.Id);
    }

    [Fact(DisplayName = RoomModerationSpec.AnsweringSomeoneElsesInvitation
                        + "anyone other than the invitee is refused as not-the-recipient")]
    public async Task Only_the_invitee_may_answer()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var bystander = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);
        var invitation = await owner.InviteAsync(room.Id, invitee);

        foreach (var answer in new[] { "accept", "reject" })
        {
            var response = await bystander.Client.PostAsync(
                $"/api/invitations/{invitation.Id}/{answer}", content: null);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"a bystander may not {answer}");
            (await response.ReadErrorAsync())?.Code.Should().Be("Invitation.NotRecipient");
        }

        (await ListInvitationsAsync(invitee)).Should().ContainSingle(
            "the invitation is still waiting for the person it was sent to");
    }

    [Fact(DisplayName = RoomModerationSpec.AnsweringTwice
                        + "answering an invitation that is no longer pending is refused as already processed")]
    public async Task An_answered_invitation_cannot_be_answered_again()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);
        var invitation = await owner.InviteAsync(room.Id, invitee);

        (await invitee.Client.PostAsync($"/api/invitations/{invitation.Id}/reject", content: null))
            .IsSuccessStatusCode.Should().BeTrue();

        foreach (var answer in new[] { "accept", "reject" })
        {
            var response = await invitee.Client.PostAsync(
                $"/api/invitations/{invitation.Id}/{answer}", content: null);

            response.IsSuccessStatusCode.Should().BeFalse();
            (await response.ReadErrorAsync())?.Code.Should().Be("Invitation.AlreadyProcessed");
        }
    }

    [Fact(DisplayName = RoomModerationSpec.InvitationToADeletedRoom
                        + "accepting an invitation to a deleted room is refused and creates no membership")]
    public async Task An_invitation_to_a_deleted_room_cannot_be_accepted()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);
        var invitation = await owner.InviteAsync(room.Id, invitee);

        (await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}")).IsSuccessStatusCode.Should().BeTrue();

        var response = await invitee.Client.PostAsync(
            $"/api/invitations/{invitation.Id}/accept", content: null);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await invitee.Client.GetAsync($"/api/Rooms/{room.Id}")).IsSuccessStatusCode
            .Should().BeFalse("there is nothing to be a member of");

        (await ListInvitationsAsync(invitee)).Should().BeEmpty(
            "an invitation to a room that no longer exists is not offered as pending");
    }

    [Fact(DisplayName = RoomModerationSpec.BannedBeforeAccepting
                        + "a ban applied after the invitation was sent refuses the acceptance")]
    public async Task A_ban_after_the_invitation_refuses_the_acceptance()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);
        var invitation = await owner.InviteAsync(room.Id, invitee);

        await owner.BanAsync(room.Id, invitee, "changed our minds");

        var response = await invitee.Client.PostAsync(
            $"/api/invitations/{invitation.Id}/accept", content: null);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.Banned");
        (await invitee.Client.GetAsync($"/api/Rooms/{room.Id}")).IsSuccessStatusCode.Should().BeFalse();
    }

    private static async Task<List<RoomInvitationDto>> ListInvitationsAsync(TestUser user) =>
        (await user.Client.GetFromJsonAsync<List<RoomInvitationDto>>("/api/me/invitations"))!;
}
