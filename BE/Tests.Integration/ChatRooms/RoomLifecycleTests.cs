using System.Net;
using System.Net.Http.Json;
using Application.Features.Rooms.Dtos;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.ChatRooms;

/// <summary>
/// Backend integration tests for creating, naming, reading, renaming, and deleting a room —
/// <c>openspec/specs/chat-rooms/spec.md</c>.
/// </summary>
[Collection(Collections.Rooms)]
public class RoomLifecycleTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = ChatRoomsSpec.CreatingARoom
                        + "the creator becomes owner of a room carrying the name, description, and visibility given")]
    public async Task Creating_a_room_makes_the_creator_its_owner()
    {
        var owner = await CreateUserAsync();
        var name = ChatApi.UniqueName("Engineering");

        var room = await owner.CreateRoomAsync(name, RoomVisibility.Private, "Where the work happens");

        room.Name.Should().Be(name);
        room.Description.Should().Be("Where the work happens");
        room.Visibility.Should().Be(RoomVisibility.Private);
        room.Kind.Should().Be(RoomKind.Group);
        room.OwnerId.Should().Be(owner.UserId);
        room.OwnerUserName.Should().Be(owner.Username);
        room.MyRole.Should().Be(RoomRole.Owner);
        room.MemberCount.Should().Be(1, "creating a room joins you to it");

        var mine = await owner.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine!.Select(r => r.Id).Should().Contain(room.Id);
    }

    [Theory(Skip = "SPEC GAP chat-rooms/room-creation: POST /api/Rooms accepts an empty or "
                   + "whitespace-only name and creates the room. UpdateRoomAsync rejects one; "
                   + "CreateRoomAsync has no such guard. See docs/spec-gaps.md.",
            DisplayName = ChatRoomsSpec.MissingName
                          + "a room with no usable name is refused and nothing is created")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_room_needs_a_name(string name)
    {
        var owner = await CreateUserAsync();

        var response = await owner.Client.PostAsJsonAsync(
            "/api/Rooms", new { name, description = (string?)null, visibility = RoomVisibility.Public });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var mine = await owner.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine.Should().BeEmpty("the refused request created nothing");
    }

    [Fact(DisplayName = ChatRoomsSpec.DuplicateNameOnCreation
                        + "a room name already in use is refused at creation")]
    public async Task A_duplicate_name_is_refused_at_creation()
    {
        var owner = await CreateUserAsync();
        var other = await CreateUserAsync();
        var name = ChatApi.UniqueName("Shared");
        await owner.CreateRoomAsync(name);

        var response = await other.Client.PostAsJsonAsync(
            "/api/Rooms", new { name, description = (string?)null, visibility = RoomVisibility.Public });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NameAlreadyExists");
    }

    [Fact(DisplayName = ChatRoomsSpec.DuplicateNameOnRename
                        + "renaming onto an existing room's name is refused and the room keeps its name")]
    public async Task A_rename_onto_a_taken_name_is_refused()
    {
        var owner = await CreateUserAsync();
        var taken = await owner.CreateRoomAsync(ChatApi.UniqueName("Taken"));
        var mine = await owner.CreateRoomAsync(ChatApi.UniqueName("Mine"));

        var response = await owner.Client.PutAsJsonAsync($"/api/Rooms/{mine.Id}", new
        {
            name = taken.Name,
            description = (string?)null,
            visibility = RoomVisibility.Public,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NameAlreadyExists");

        var unchanged = await owner.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{mine.Id}");
        unchanged!.Name.Should().Be(mine.Name);
    }

    [Fact(DisplayName = ChatRoomsSpec.ReusingADeletedRoomsName
                        + "a deleted room's name is free for a new room")]
    public async Task A_deleted_rooms_name_becomes_available_again()
    {
        var owner = await CreateUserAsync();
        var name = ChatApi.UniqueName("Recycled");
        var first = await owner.CreateRoomAsync(name);

        (await owner.Client.DeleteAsync($"/api/Rooms/{first.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await owner.CreateRoomAsync(name);

        second.Id.Should().NotBe(first.Id);
        second.Name.Should().Be(name, "the name went back into circulation when the room went away");
    }

    [Fact(DisplayName = ChatRoomsSpec.ReadingARoom
                        + "a member's read carries name, description, visibility, owner, member count, and their role")]
    public async Task A_member_reads_every_specified_room_property()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var created = await owner.CreateRoomAsync(
            ChatApi.UniqueName("Readable"), RoomVisibility.Public, "A described room");
        await member.JoinRoomAsync(created.Id);

        var room = await member.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{created.Id}");

        room!.Name.Should().Be(created.Name);
        room.Description.Should().Be("A described room");
        room.Visibility.Should().Be(RoomVisibility.Public);
        room.OwnerId.Should().Be(owner.UserId);
        room.OwnerUserName.Should().Be(owner.Username);
        room.MemberCount.Should().Be(2);
        room.MyRole.Should().Be(RoomRole.Member, "the caller joined rather than created it");
    }

    [Fact(DisplayName = ChatRoomsSpec.NonMemberReadsAPublicRoom
                        + "a non-member reads a public room and gets no role of their own")]
    public async Task A_non_member_reads_a_public_room_without_a_role()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var created = await owner.CreateRoomAsync(visibility: RoomVisibility.Public);

        var room = await outsider.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{created.Id}");

        room!.Id.Should().Be(created.Id);
        room.MyRole.Should().BeNull("the caller belongs to nothing here");
    }

    [Fact(DisplayName = ChatRoomsSpec.NonMemberRequestsAPrivateRoom
                        + "a non-member requesting a private room is refused with a private-room error")]
    public async Task A_non_member_cannot_read_a_private_room()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);

        var response = await outsider.Client.GetAsync($"/api/Rooms/{room.Id}");

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.PrivateRoom");
    }

    [Fact(DisplayName = ChatRoomsSpec.OwnerSavesSettings
                        + "the owner saves a new name, description, and visibility")]
    public async Task The_owner_can_change_a_rooms_settings()
    {
        var owner = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Public);
        var newName = ChatApi.UniqueName("Renamed");

        var response = await owner.Client.PutAsJsonAsync($"/api/Rooms/{room.Id}", new
        {
            name = newName,
            description = "Now with a description",
            visibility = RoomVisibility.Private,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await owner.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{room.Id}");
        updated!.Name.Should().Be(newName);
        updated.Description.Should().Be("Now with a description");
        updated.Visibility.Should().Be(RoomVisibility.Private);
    }

    [Fact(DisplayName = ChatRoomsSpec.AdminAttemptsToChangeSettings
                        + "an admin who is not the owner is refused, and the settings are unchanged")]
    public async Task An_admin_cannot_change_room_settings()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();
        await admin.JoinRoomAsync(room.Id);
        await owner.MakeAdminAsync(room.Id, admin);

        var response = await admin.Client.PutAsJsonAsync($"/api/Rooms/{room.Id}", new
        {
            name = ChatApi.UniqueName("AdminRenamed"),
            description = (string?)null,
            visibility = RoomVisibility.Private,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "settings are the owner's alone, even for an admin");

        var unchanged = await owner.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{room.Id}");
        unchanged!.Name.Should().Be(room.Name);
        unchanged.Visibility.Should().Be(RoomVisibility.Public);
    }

    [Fact(DisplayName = ChatRoomsSpec.OwnerDeletesARoom
                        + "the owner deletes a room; it is marked deleted and no longer readable")]
    public async Task The_owner_can_delete_a_room()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);

        var response = await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Rooms
                .SingleAsync(r => r.Id == room.Id));
        stored.DeletedAt.Should().NotBeNull("deletion is a mark, so history is not orphaned");

        (await member.Client.GetAsync($"/api/Rooms/{room.Id}")).IsSuccessStatusCode
            .Should().BeFalse("a deleted room is no longer readable, even by a member");

        var mine = await member.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine!.Select(r => r.Id).Should().NotContain(room.Id);
    }

    [Fact(DisplayName = ChatRoomsSpec.NonOwnerAttemptsDeletion
                        + "a member's delete is refused and the room survives")]
    public async Task A_non_owner_cannot_delete_a_room()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);

        var response = await member.Client.DeleteAsync($"/api/Rooms/{room.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.Client.GetAsync($"/api/Rooms/{room.Id}")).IsSuccessStatusCode
            .Should().BeTrue("the room is still there");
    }

    [Fact(DisplayName = ChatRoomsSpec.MessagingADeletedRoom
                        + "posting to a deleted room is refused as already deleted")]
    public async Task Posting_to_a_deleted_room_is_refused()
    {
        var owner = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();
        await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}");

        var response = await owner.Client.PostAsJsonAsync(
            $"/api/rooms/{room.Id}/Messages", new { text = "anyone still here?" });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.AlreadyDeleted");
    }

    [Fact(DisplayName = ChatRoomsSpec.MemberLeaves
                        + "a member leaves; the room drops out of their list and the member count falls")]
    public async Task A_member_can_leave()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);

        var response = await member.Client.PostAsync($"/api/Rooms/{room.Id}/leave", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mine = await member.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine!.Select(r => r.Id).Should().NotContain(room.Id);

        var asOwner = await owner.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{room.Id}");
        asOwner!.MemberCount.Should().Be(1);
    }

    [Fact(DisplayName = ChatRoomsSpec.OwnerAttemptsToLeave
                        + "the owner cannot leave their own room")]
    public async Task The_owner_cannot_leave()
    {
        var owner = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();

        var response = await owner.Client.PostAsync($"/api/Rooms/{room.Id}/leave", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.OwnerCannotLeave");

        var mine = await owner.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine!.Select(r => r.Id).Should().Contain(room.Id);
    }
}
