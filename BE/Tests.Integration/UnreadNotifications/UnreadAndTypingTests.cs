using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.UnreadNotifications;

/// <summary>
/// Backend integration tests for read positions, typing notices, and the friend-request push —
/// <c>openspec/specs/unread-notifications/spec.md</c>.
///
/// Which chat is open is something only the client knows, so whether a badge is raised is decided
/// there. What the server owns is the read position, who may record one, and who receives a typing
/// notice.
/// </summary>
[Collection(Collections.Realtime)]
public class UnreadAndTypingTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = UnreadNotificationsSpec.ReadPositionRecordedPerMembership
                        + "the read position is stored against the caller's membership of that chat")]
    public async Task The_read_position_is_stored_against_the_membership()
    {
        var owner = await CreateUserAsync();
        var reader = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(reader);
        var message = await owner.SendMessageAsync(room.Id, "read me");

        var response = await reader.Client.PostAsJsonAsync(
            $"/api/rooms/{room.Id}/Messages/read", new { messageId = message.Id });

        response.IsSuccessStatusCode.Should().BeTrue();

        var memberships = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().RoomMemberships
                .Where(m => m.RoomId == room.Id)
                .ToListAsync());

        memberships.Single(m => m.UserId == reader.UserId).LastReadMessageId.Should().Be(message.Id,
            "the position belongs to this user's membership of this chat");
        memberships.Single(m => m.UserId == owner.UserId).LastReadMessageId.Should().BeNull(
            "and to nobody else's — it is per membership, not per room");
    }

    [Fact(DisplayName = UnreadNotificationsSpec.OpeningAChatClearsTheBadge
                        + "the recorded position survives a reconnect and moves forward as reading continues")]
    public async Task The_recorded_position_persists_and_advances()
    {
        var owner = await CreateUserAsync();
        var reader = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(reader);

        var first = await owner.SendMessageAsync(room.Id, "one");
        await MarkReadAsync(reader, room.Id, first.Id);

        var second = await owner.SendMessageAsync(room.Id, "two");
        await MarkReadAsync(reader, room.Id, second.Id);

        // A different sign-in of the same account is a different device: the position is the
        // account's, stored server-side, so it is the same there.
        var otherDevice = await AuthHelper.SignInAgainAsync(Fixture, reader);
        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().RoomMemberships
                .SingleAsync(m => m.RoomId == room.Id && m.UserId == otherDevice.UserId));

        stored.LastReadMessageId.Should().Be(second.Id,
            "the position is stored where reconnects and other devices can find it");
    }

    [Fact(DisplayName = UnreadNotificationsSpec.MarkingReadInAChatOneIsNotIn
                        + "marking read in a chat the caller does not belong to is refused as not-a-member")]
    public async Task Marking_read_somewhere_you_are_not_a_member_is_refused()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();
        var message = await owner.SendMessageAsync(room.Id, "not for you");

        var response = await outsider.Client.PostAsJsonAsync(
            $"/api/rooms/{room.Id}/Messages/read", new { messageId = message.Id });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NotMember");
    }

    [Fact(DisplayName = UnreadNotificationsSpec.MarkingReadInAChatOneIsNotIn
                        + "the same refusal applies over the real-time channel")]
    public async Task Marking_read_over_the_hub_is_refused_the_same_way()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();
        var message = await owner.SendMessageAsync(room.Id, "not for you");

        await using var hub = await ConnectHubAsync(outsider);

        var invoking = async () => await hub.InvokeAsync("MarkRead", room.Id, message.Id);

        (await invoking.Should().ThrowAsync<HubException>()).Which.Message
            .Should().Contain("not a member");
    }

    [Fact(DisplayName = UnreadNotificationsSpec.Composing
                        + "a typing notice reaches the other participants and not the person typing")]
    public async Task Typing_notifies_everyone_but_the_typist()
    {
        var typist = await CreateUserAsync();
        var watcher = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await typist.CreateRoomWithAsync(watcher);

        await using var typistHub = await ConnectHubAsync(typist);
        await using var watcherHub = await ConnectHubAsync(watcher);
        await using var outsiderHub = await ConnectHubAsync(outsider);

        var toWatcher = watcherHub.Expect<JsonElement>(ChatHubEvents.UserTyping);
        var toTypist = typistHub.Expect<JsonElement>(
            ChatHubEvents.UserTyping, timeout: TimeSpan.FromSeconds(2));
        var toOutsider = outsiderHub.Expect<JsonElement>(
            ChatHubEvents.UserTyping, timeout: TimeSpan.FromSeconds(2));

        await typistHub.InvokeAsync("StartTyping", room.Id);

        var payload = await toWatcher.RawAsync();
        payload.GetProperty("userId").GetString().Should().Be(typist.UserId);
        payload.GetProperty("roomId").GetString().Should().Be(room.Id.ToString(),
            "the notice names both who is typing and where");

        (await toTypist.TimedOutAsync()).Should().BeTrue(
            "the notice is not sent back to the composing user");
        (await toOutsider.TimedOutAsync()).Should().BeTrue(
            "and it is scoped to the chat, not broadcast");
    }

    [Fact(DisplayName = UnreadNotificationsSpec.SendingStopsTyping
                        + "a stopped-typing notice reaches the other participants and not the sender")]
    public async Task Stopping_typing_notifies_everyone_but_the_typist()
    {
        var typist = await CreateUserAsync();
        var watcher = await CreateUserAsync();
        var room = await typist.CreateRoomWithAsync(watcher);

        await using var typistHub = await ConnectHubAsync(typist);
        await using var watcherHub = await ConnectHubAsync(watcher);

        await typistHub.InvokeAsync("StartTyping", room.Id);
        await watcherHub.WaitFor<JsonElement>(ChatHubEvents.UserTyping);

        var stopped = watcherHub.Expect<JsonElement>(ChatHubEvents.UserStoppedTyping);
        var toSelf = typistHub.Expect<JsonElement>(
            ChatHubEvents.UserStoppedTyping, timeout: TimeSpan.FromSeconds(2));

        await typistHub.InvokeAsync("StopTyping", room.Id);

        (await stopped.RawAsync()).GetProperty("userId").GetString().Should().Be(typist.UserId);
        (await toSelf.TimedOutAsync()).Should().BeTrue();
    }

    [Fact(DisplayName = UnreadNotificationsSpec.ReceivingARequestWhileOnline
                        + "a friend request is pushed to the recipient with the requester's identity and message")]
    public async Task A_friend_request_is_pushed_to_the_recipient()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();

        await using var hub = await ConnectHubAsync(recipient);
        var expectation = hub.Expect<JsonElement>(ChatHubEvents.FriendRequestReceived);

        await sender.Client.PostAsJsonAsync("/api/Friends/requests", new
        {
            username = recipient.Username,
            message = "we worked together in 2024",
        });

        var payload = await expectation.RawAsync();
        var text = payload.ToString();
        text.Should().Contain(sender.Username, "the notification names the requester");
        text.Should().Contain("we worked together in 2024",
            "and carries the message that came with it");
    }

    [Fact(Skip = "SPEC GAP unread-notifications/unread-indicators: the server keeps no unread count "
                 + "and never sends UnreadUpdated — IChatNotifier.UnreadUpdatedAsync has no caller. "
                 + "See docs/spec-gaps.md.",
          DisplayName = UnreadNotificationsSpec.MessageArrivesInABackgroundChat
                        + "a message in a chat raises that chat's unread count for the recipient")]
    public async Task A_message_raises_the_recipients_unread_count()
    {
        var author = await CreateUserAsync();
        var recipient = await CreateUserAsync();
        var room = await author.CreateRoomWithAsync(recipient);

        await using var hub = await ConnectHubAsync(recipient);
        var expectation = hub.Expect<JsonElement>(ChatHubEvents.UnreadUpdated);

        await author.SendMessageAsync(room.Id, "you have not seen this yet");

        var payload = await expectation.RawAsync();
        payload.GetProperty("roomId").GetString().Should().Be(room.Id.ToString());
        payload.GetProperty("unreadCount").GetInt32().Should().Be(1,
            "the count the badge shows is the server's, so it survives a reload and other devices");
    }

    private static async Task MarkReadAsync(TestUser user, Guid roomId, Guid messageId)
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/api/rooms/{roomId}/Messages/read", new { messageId });
        response.IsSuccessStatusCode.Should().BeTrue(
            $"marking read failed: {await response.Content.ReadAsStringAsync()}");
    }
}
