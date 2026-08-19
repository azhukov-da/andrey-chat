using System.Net.Http.Json;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.ContactsAndBlocking;

/// <summary>
/// Backend integration tests for blocking and unblocking —
/// <c>openspec/specs/contacts-and-blocking/spec.md</c>. What a block does to an existing direct
/// chat is the subject here; what it does to opening a new one is under
/// <c>openspec/specs/direct-messages/spec.md</c>.
/// </summary>
[Collection(Collections.Social)]
public class BlockingTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = ContactsAndBlockingSpec.BlockingFromTheContactList
                        + "blocking freezes the direct chat and refuses further messages from either side")]
    public async Task Blocking_freezes_the_chat_for_both_directions()
    {
        var (blocker, blocked, chat) = await ChattingPairAsync();

        var response = await blocker.Client.PostAsync(
            $"/api/Friends/blocks/{blocked.UserId}", content: null);

        response.IsSuccessStatusCode.Should().BeTrue();

        var frozen = await blocker.Client.GetFromJsonAsync<Application.Features.Rooms.Dtos.RoomDto>(
            $"/api/Rooms/{chat.Id}");
        frozen!.IsFrozen.Should().BeTrue("the chat is marked frozen, which is what the UI reads");

        (await SendAsync(blocker, chat.Id)).IsSuccessStatusCode.Should().BeFalse(
            "the block stops the blocker writing too, not only the blocked user");
        (await SendAsync(blocked, chat.Id)).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.BlockedUserAttemptsToWrite
                        + "the blocked user's send in the existing chat is refused as forbidden")]
    public async Task The_blocked_users_send_is_forbidden()
    {
        var (blocker, blocked, chat) = await ChattingPairAsync();
        await blocker.Client.PostAsync($"/api/Friends/blocks/{blocked.UserId}", content: null);

        var response = await SendAsync(blocked, chat.Id);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Authorization.Forbidden");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.HistoryRemainsReadable
                        + "both users can still read the frozen chat's history")]
    public async Task A_frozen_chats_history_stays_readable()
    {
        var (blocker, blocked, chat) = await ChattingPairAsync();
        var earlier = await blocker.SendMessageAsync(chat.Id, "said before the block");
        await blocker.Client.PostAsync($"/api/Friends/blocks/{blocked.UserId}", content: null);

        (await blocker.GetHistoryAsync(chat.Id)).Items.Should().Contain(m => m.Id == earlier.Id,
            "blocking hides nothing that was already said");
        (await blocked.GetHistoryAsync(chat.Id)).Items.Should().Contain(m => m.Id == earlier.Id,
            "and the blocked user keeps their copy of the conversation too");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.BlockingOneself
                        + "blocking one's own account is rejected")]
    public async Task Blocking_yourself_is_rejected()
    {
        var user = await CreateUserAsync();

        var response = await user.Client.PostAsync(
            $"/api/Friends/blocks/{user.UserId}", content: null);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Block.CannotBlockSelf");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.BlockingFromTheContactList
                        + "blocking someone already blocked is rejected")]
    public async Task Blocking_twice_is_rejected()
    {
        var blocker = await CreateUserAsync();
        var blocked = await CreateUserAsync();
        (await blocker.Client.PostAsync($"/api/Friends/blocks/{blocked.UserId}", content: null))
            .IsSuccessStatusCode.Should().BeTrue();

        var again = await blocker.Client.PostAsync(
            $"/api/Friends/blocks/{blocked.UserId}", content: null);

        again.IsSuccessStatusCode.Should().BeFalse();
        (await again.ReadErrorAsync())?.Code.Should().Be("Block.AlreadyBlocked");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.Unblocking_
                        + "unblocking unfreezes the chat and messaging works again between friends")]
    public async Task Unblocking_restores_the_conversation()
    {
        var (blocker, blocked, chat) = await ChattingPairAsync();
        await blocker.Client.PostAsync($"/api/Friends/blocks/{blocked.UserId}", content: null);
        (await SendAsync(blocked, chat.Id)).IsSuccessStatusCode.Should().BeFalse();

        var response = await blocker.Client.DeleteAsync($"/api/Friends/blocks/{blocked.UserId}");

        response.IsSuccessStatusCode.Should().BeTrue();

        var thawed = await blocker.Client.GetFromJsonAsync<Application.Features.Rooms.Dtos.RoomDto>(
            $"/api/Rooms/{chat.Id}");
        thawed!.IsFrozen.Should().BeFalse();

        (await SendAsync(blocked, chat.Id)).IsSuccessStatusCode.Should().BeTrue(
            "they are still friends, so removing the block restores messaging");
        (await SendAsync(blocker, chat.Id)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.UnblockingSomeoneWhoIsNotBlocked
                        + "unblocking someone who is not blocked is rejected")]
    public async Task Unblocking_a_user_who_is_not_blocked_is_rejected()
    {
        var user = await CreateUserAsync();
        var other = await CreateUserAsync();

        var response = await user.Client.DeleteAsync($"/api/Friends/blocks/{other.UserId}");

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Block.NotBlocked");
    }

    /// <summary>Two friends with a direct chat already open between them.</summary>
    private async Task<(TestUser First, TestUser Second, Application.Features.Rooms.Dtos.RoomDto Chat)>
        ChattingPairAsync()
    {
        var first = await CreateUserAsync();
        var second = await CreateUserAsync();
        await first.BefriendAsync(second);
        var chat = await first.OpenDirectChatAsync(second);
        return (first, second, chat);
    }

    private static Task<HttpResponseMessage> SendAsync(TestUser user, Guid roomId) =>
        user.Client.PostAsJsonAsync($"/api/rooms/{roomId}/Messages", new { text = "still talking?" });
}
