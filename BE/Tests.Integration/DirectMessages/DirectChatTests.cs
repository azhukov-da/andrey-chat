using System.Net.Http.Json;
using Application.Common;
using Application.Features.Rooms.Dtos;
using Domain.Enums;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.DirectMessages;

/// <summary>
/// Backend integration tests for one-to-one chats — <c>openspec/specs/direct-messages/spec.md</c>:
/// what it takes to open one, what closes it, and how far its resemblance to a room goes.
/// </summary>
[Collection(Collections.Social)]
public class DirectChatTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = DirectMessagesSpec.MessagingAFriendForTheFirstTime
                        + "a first message to a confirmed contact creates a two-member direct chat")]
    public async Task Opening_a_chat_with_a_friend_creates_a_two_member_direct_room()
    {
        var one = await CreateUserAsync();
        var other = await CreateUserAsync();
        await one.BefriendAsync(other);

        var chat = await one.OpenDirectChatAsync(other);

        chat.Kind.Should().Be(RoomKind.Direct);
        chat.Visibility.Should().Be(RoomVisibility.Private);
        chat.MemberCount.Should().Be(2);
        chat.OtherUserId.Should().Be(other.UserId,
            "the caller's view of a direct chat names the person on the other side");
        chat.IsFrozen.Should().BeFalse();

        // Both participants are in it, which is what makes them both receive its events.
        var members = await one.Client.GetFromJsonAsync<List<RoomMemberDto>>(
            $"/api/Rooms/{chat.Id}/members");
        members!.Select(m => m.UserId).Should().BeEquivalentTo([one.UserId, other.UserId]);
    }

    [Fact(DisplayName = DirectMessagesSpec.ReopeningAnExistingChat
                        + "opening again returns the same chat with its history, rather than a second one")]
    public async Task Reopening_returns_the_existing_chat()
    {
        var one = await CreateUserAsync();
        var other = await CreateUserAsync();
        await one.BefriendAsync(other);

        var first = await one.OpenDirectChatAsync(other);
        var message = await one.SendMessageAsync(first.Id, "said the first time round");

        var reopenedBySameUser = await one.OpenDirectChatAsync(other);
        var openedByTheOther = await other.OpenDirectChatAsync(one);

        reopenedBySameUser.Id.Should().Be(first.Id);
        openedByTheOther.Id.Should().Be(first.Id,
            "the pair have one chat, whichever of them opens it");

        (await other.GetHistoryAsync(first.Id)).Items.Should().Contain(m => m.Id == message.Id,
            "and it still holds what was said");
    }

    [Fact(DisplayName = DirectMessagesSpec.NotFriends
                        + "opening a chat with someone who is not a confirmed friend is refused")]
    public async Task A_chat_needs_a_confirmed_friendship()
    {
        var one = await CreateUserAsync();
        var stranger = await CreateUserAsync();

        var withAStranger = await OpenAsync(one, stranger);
        withAStranger.IsSuccessStatusCode.Should().BeFalse();
        (await withAStranger.ReadErrorAsync())?.Code.Should().Be("Friendship.NotAccepted");

        // A request that has not been accepted is not a friendship either.
        await one.Client.PostAsJsonAsync(
            "/api/Friends/requests", new { username = stranger.Username, message = (string?)null });

        var whilePending = await OpenAsync(one, stranger);
        whilePending.IsSuccessStatusCode.Should().BeFalse(
            "the friendship becomes effective on acceptance, not on asking");
    }

    [Theory(DisplayName = DirectMessagesSpec.BlockedInEitherDirection
                          + "a block in either direction refuses a new chat")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_block_in_either_direction_refuses_opening(bool blockerOpens)
    {
        var blocker = await CreateUserAsync();
        var blocked = await CreateUserAsync();
        await blocker.BefriendAsync(blocked);
        await blocker.Client.PostAsync($"/api/Friends/blocks/{blocked.UserId}", content: null);

        var response = blockerOpens
            ? await OpenAsync(blocker, blocked)
            : await OpenAsync(blocked, blocker);

        response.IsSuccessStatusCode.Should().BeFalse(
            "the direction of the block does not decide who is stopped");
    }

    [Fact(DisplayName = DirectMessagesSpec.ChatWithSelf
                        + "opening a chat with one's own username is refused")]
    public async Task A_chat_with_yourself_is_refused()
    {
        var user = await CreateUserAsync();

        var response = await OpenAsync(user, user);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("DirectChat.CannotChatWithSelf");
    }

    [Fact(DisplayName = DirectMessagesSpec.NotInTheCatalog
                        + "no direct chat appears in the public catalog")]
    public async Task Direct_chats_never_reach_the_catalog()
    {
        var one = await CreateUserAsync();
        var other = await CreateUserAsync();
        await one.BefriendAsync(other);
        var chat = await one.OpenDirectChatAsync(other);

        foreach (var browser in new[] { one, other })
        {
            var catalog = await browser.Client.GetFromJsonAsync<Paged<RoomDto>>(
                "/api/Rooms/public?pageSize=100");
            catalog!.Items.Select(r => r.Id).Should().NotContain(chat.Id,
                "a direct chat is private and belongs to nobody else's browsing");
        }
    }

    [Fact(DisplayName = DirectMessagesSpec.ListingDirectChats
                        + "a direct chat is listed alongside the user's rooms, for both participants")]
    public async Task Direct_chats_are_listed_with_the_users_rooms()
    {
        var one = await CreateUserAsync();
        var other = await CreateUserAsync();
        await one.BefriendAsync(other);
        var chat = await one.OpenDirectChatAsync(other);

        foreach (var participant in new[] { one, other })
        {
            var mine = await participant.Client.GetFromJsonAsync<List<RoomDto>>("/api/Rooms/mine");
            mine!.Should().Contain(r => r.Id == chat.Id && r.Kind == RoomKind.Direct,
                "the sidebar builds its direct-chats section from this list");
        }
    }

    [Fact(DisplayName = DirectMessagesSpec.FeatureParity
                        + "a direct chat supports text, replies, attachments, and history as a room does")]
    public async Task A_direct_chat_does_everything_a_room_does()
    {
        var one = await CreateUserAsync();
        var other = await CreateUserAsync();
        await one.BefriendAsync(other);
        var chat = await one.OpenDirectChatAsync(other);

        var text = await one.SendMessageAsync(chat.Id, "plain text with an emoji \U0001F642");
        var reply = await other.SendMessageAsync(chat.Id, "a reply", text.Id);
        var upload = await one.UploadAsync(
            chat.Id, "shared.txt", "text/plain", "contents"u8.ToArray());

        var edited = await other.Client.PatchAsJsonAsync(
            $"/api/messages/{reply.Id}", new { text = "a reply, revised" });
        edited.IsSuccessStatusCode.Should().BeTrue("editing works here too");

        var history = await other.GetHistoryAsync(chat.Id);
        history.Items.Select(m => m.Id).Should().Contain([text.Id, reply.Id, upload.Id]);
        history.Items.Single(m => m.Id == reply.Id).ReplyToMessageId.Should().Be(text.Id);
        history.Items.Single(m => m.Id == upload.Id).Attachments.Should()
            .ContainSingle(a => a.FileName == "shared.txt");
    }

    [Fact(DisplayName = DirectMessagesSpec.SendingInAFrozenChat
                        + "sending in a frozen chat is refused for both participants")]
    public async Task A_frozen_chat_refuses_new_messages()
    {
        var (blocker, blocked, chat) = await FrozenChatAsync();

        (await SendAsync(blocked, chat.Id)).IsSuccessStatusCode.Should().BeFalse();
        (await SendAsync(blocker, chat.Id)).IsSuccessStatusCode.Should().BeFalse(
            "freezing is a property of the chat, not a punishment aimed at one side");
    }

    [Fact(DisplayName = DirectMessagesSpec.ReadingAFrozenChat
                        + "a frozen chat can still be opened and read by both participants")]
    public async Task A_frozen_chat_is_still_readable()
    {
        var one = await CreateUserAsync();
        var other = await CreateUserAsync();
        await one.BefriendAsync(other);
        var chat = await one.OpenDirectChatAsync(other);
        var said = await one.SendMessageAsync(chat.Id, "before it froze");
        await one.Client.PostAsync($"/api/Friends/blocks/{other.UserId}", content: null);

        foreach (var participant in new[] { one, other })
        {
            var read = await participant.Client.GetFromJsonAsync<RoomDto>($"/api/Rooms/{chat.Id}");
            read!.IsFrozen.Should().BeTrue();

            (await participant.GetHistoryAsync(chat.Id)).Items.Should().Contain(m => m.Id == said.Id);
        }
    }

    [Fact(DisplayName = DirectMessagesSpec.UnblockingRestoresMessaging
                        + "unblocking thaws the chat and both sides can write again")]
    public async Task Unblocking_thaws_the_chat()
    {
        var (blocker, blocked, chat) = await FrozenChatAsync();

        (await blocker.Client.DeleteAsync($"/api/Friends/blocks/{blocked.UserId}"))
            .IsSuccessStatusCode.Should().BeTrue();

        (await SendAsync(blocked, chat.Id)).IsSuccessStatusCode.Should().BeTrue();
        (await SendAsync(blocker, chat.Id)).IsSuccessStatusCode.Should().BeTrue();
    }

    private async Task<(TestUser Blocker, TestUser Blocked, RoomDto Chat)> FrozenChatAsync()
    {
        var blocker = await CreateUserAsync();
        var blocked = await CreateUserAsync();
        await blocker.BefriendAsync(blocked);
        var chat = await blocker.OpenDirectChatAsync(blocked);
        await blocker.Client.PostAsync($"/api/Friends/blocks/{blocked.UserId}", content: null);
        return (blocker, blocked, chat);
    }

    private static Task<HttpResponseMessage> OpenAsync(TestUser user, TestUser other) =>
        user.Client.PostAsJsonAsync("/api/DirectChats", new { username = other.Username });

    private static Task<HttpResponseMessage> SendAsync(TestUser user, Guid roomId) =>
        user.Client.PostAsJsonAsync($"/api/rooms/{roomId}/Messages", new { text = "anything" });
}
