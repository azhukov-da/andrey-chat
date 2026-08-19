using System.Net;
using System.Net.Http.Json;
using Application.Common;
using Application.Features.Rooms.Dtos;
using Domain.Enums;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.ChatRooms;

/// <summary>
/// Backend integration tests for the public catalog and for joining what it lists —
/// <c>openspec/specs/chat-rooms/spec.md</c>.
/// </summary>
[Collection(Collections.Rooms)]
public class CatalogAndJoiningTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = ChatRoomsSpec.BrowsingTheCatalog
                        + "the catalog lists public rooms with description and member count, and pages")]
    public async Task The_catalog_lists_public_rooms_and_pages_through_them()
    {
        var owner = await CreateUserAsync();
        var joiner = await CreateUserAsync();

        var described = await owner.CreateRoomAsync(
            ChatApi.UniqueName("Described"), RoomVisibility.Public, "It has a description");
        await joiner.JoinRoomAsync(described.Id);

        for (var i = 0; i < 3; i++) await owner.CreateRoomAsync(visibility: RoomVisibility.Public);

        var firstPage = await BrowseAsync(owner, page: 1, pageSize: 2);

        firstPage.Items.Should().HaveCount(2, "the page size caps what one request returns");
        firstPage.TotalCount.Should().BeGreaterThanOrEqualTo(4);
        firstPage.HasNextPage.Should().BeTrue("more rooms remain to be loaded");

        var everything = await BrowseAsync(owner, page: 1, pageSize: 50);
        var entry = everything.Items.Single(r => r.Id == described.Id);
        entry.Description.Should().Be("It has a description");
        entry.MemberCount.Should().Be(2, "the owner and the user who joined");
    }

    [Fact(DisplayName = ChatRoomsSpec.Searching
                        + "search matches name or description, case-insensitively, and excludes the rest")]
    public async Task Search_matches_name_or_description_ignoring_case()
    {
        var owner = await CreateUserAsync();
        var term = Guid.NewGuid().ToString("N")[..8];

        var byName = await owner.CreateRoomAsync($"Room-{term}-name");
        var byDescription = await owner.CreateRoomAsync(
            ChatApi.UniqueName("Plain"), RoomVisibility.Public, $"mentions {term} in the description");
        var unrelated = await owner.CreateRoomAsync(ChatApi.UniqueName("Unrelated"));

        var results = await SearchAsync(owner, term.ToUpperInvariant());

        results.Items.Select(r => r.Id).Should()
            .Contain(byName.Id, "the term is in the name")
            .And.Contain(byDescription.Id, "the term is in the description")
            .And.NotContain(unrelated.Id, "the term is in neither");
    }

    [Fact(DisplayName = ChatRoomsSpec.PrivateRoomsHidden
                        + "a private room is absent from the catalog even for its own members")]
    public async Task Private_rooms_never_appear_in_the_catalog()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var priv = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);

        (await BrowseAsync(owner)).Items.Select(r => r.Id).Should().NotContain(priv.Id,
            "not even its owner sees it listed");
        (await BrowseAsync(outsider)).Items.Select(r => r.Id).Should().NotContain(priv.Id);

        var anonymous = App.CreateClient();
        var asVisitor = await anonymous.GetFromJsonAsync<Paged<RoomDto>>("/api/Rooms/public?pageSize=100");
        asVisitor!.Items.Select(r => r.Id).Should().NotContain(priv.Id);
    }

    [Fact(DisplayName = ChatRoomsSpec.CatalogAlreadyAMember
                        + "a catalog entry the caller already belongs to carries their role, so it can offer Open")]
    public async Task A_catalog_entry_reports_the_callers_membership()
    {
        var owner = await CreateUserAsync();
        var joiner = await CreateUserAsync();
        var joined = await owner.CreateRoomAsync();
        var notJoined = await owner.CreateRoomAsync();
        await joiner.JoinRoomAsync(joined.Id);

        var catalog = await BrowseAsync(joiner);

        catalog.Items.Single(r => r.Id == joined.Id).MyRole.Should().Be(RoomRole.Member,
            "the client decides between Open and Join from the caller's own role on the entry");
        catalog.Items.Single(r => r.Id == notJoined.Id).MyRole.Should().BeNull();
    }

    [Fact(DisplayName = ChatRoomsSpec.Joining_
                        + "joining a public room makes the caller a Member and puts the room in their list")]
    public async Task Joining_a_public_room_makes_the_caller_a_member()
    {
        var owner = await CreateUserAsync();
        var joiner = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Public);

        var response = await joiner.Client.PostAsync($"/api/Rooms/{room.Id}/join", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var mine = await joiner.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine!.Select(r => r.Id).Should().Contain(room.Id);

        var read = await joiner.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{room.Id}");
        read!.MyRole.Should().Be(RoomRole.Member);

        // Membership is what real-time delivery is scoped by, and it is in force straight away:
        // posting is a member-only operation and it succeeds on the next call.
        var posted = await joiner.SendMessageAsync(room.Id, "just arrived");
        posted.AuthorId.Should().Be(joiner.UserId);
    }

    [Fact(DisplayName = ChatRoomsSpec.BannedFromTheRoom
                        + "a banned user's join is rejected with a banned error")]
    public async Task A_banned_user_cannot_join()
    {
        var owner = await CreateUserAsync();
        var banned = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(banned);
        await owner.BanAsync(room.Id, banned, "for the tests");

        var response = await banned.Client.PostAsync($"/api/Rooms/{room.Id}/join", content: null);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.Banned");
    }

    [Fact(DisplayName = ChatRoomsSpec.JoinAlreadyAMember
                        + "joining a room the caller already belongs to is rejected as already a member")]
    public async Task Joining_twice_is_rejected()
    {
        var owner = await CreateUserAsync();
        var joiner = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(joiner);

        var response = await joiner.Client.PostAsync($"/api/Rooms/{room.Id}/join", content: null);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.AlreadyMember");
    }

    [Fact(DisplayName = ChatRoomsSpec.PrivateRoomJoinAttempt
                        + "joining a private room directly is rejected; access needs an invitation")]
    public async Task A_private_room_cannot_be_joined_directly()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync(visibility: RoomVisibility.Private);

        var response = await outsider.Client.PostAsync($"/api/Rooms/{room.Id}/join", content: null);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.PrivateRoom");

        var mine = await outsider.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
        mine!.Select(r => r.Id).Should().NotContain(room.Id);
    }

    private static async Task<Paged<RoomDto>> BrowseAsync(TestUser user, int page = 1, int pageSize = 100) =>
        (await user.Client.GetFromJsonAsync<Paged<RoomDto>>(
            $"/api/Rooms/public?page={page}&pageSize={pageSize}"))!;

    private static async Task<Paged<RoomDto>> SearchAsync(TestUser user, string search) =>
        (await user.Client.GetFromJsonAsync<Paged<RoomDto>>(
            $"/api/Rooms/public?search={Uri.EscapeDataString(search)}&pageSize=100"))!;
}
