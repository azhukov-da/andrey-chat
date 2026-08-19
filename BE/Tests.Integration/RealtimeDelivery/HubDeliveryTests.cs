using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Features.Messages.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Tests.Integration.Harness;

namespace Tests.Integration.RealtimeDelivery;

/// <summary>
/// Backend integration tests for the hub — <c>openspec/specs/realtime-delivery/spec.md</c>.
///
/// Every assertion here is about which connections a server-pushed event reaches, which is what
/// group enrolment decides and what no client can compensate for. Each expectation is claimed with
/// <c>Expect</c> before the action that causes it, so nothing here sleeps.
/// </summary>
[Collection(Collections.Realtime)]
public class HubDeliveryTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = RealtimeDeliverySpec.ConnectionAfterSignIn
                        + "a signed-in user's access token establishes a hub connection")]
    public async Task A_signed_in_user_can_connect()
    {
        var user = await CreateUserAsync();

        await using var hub = await ConnectHubAsync(user);

        hub.Connection.State.Should().Be(Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected);
    }

    [Fact(DisplayName = RealtimeDeliverySpec.UnauthenticatedConnect
                        + "a connection attempt with no access token is refused")]
    public async Task An_unauthenticated_connection_is_refused()
    {
        var user = await CreateUserAsync();
        var withoutAToken = user with { AccessToken = null };

        var connecting = async () => await ConnectHubAsync(withoutAToken);

        await connecting.Should().ThrowAsync<Exception>(
            "the hub carries [Authorize], so an anonymous socket never becomes a connection");
    }

    [Fact(DisplayName = RealtimeDeliverySpec.SubscriptionsOnConnect
                        + "connecting enrols the user in their personal group and every room they belong to")]
    public async Task Connecting_enrols_the_user_in_their_groups()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var first = await owner.CreateRoomWithAsync(member);
        var second = await owner.CreateRoomWithAsync(member);

        // Connecting after the memberships exist is the case the enrolment query covers.
        await using var hub = await ConnectHubAsync(member);

        foreach (var room in new[] { first, second })
        {
            var expectation = hub.Expect<MessageDto>(ChatHubEvents.MessageReceived);
            await owner.SendMessageAsync(room.Id, $"posted in {room.Name}");
            (await expectation.ValueAsync()).RoomId.Should().Be(room.Id,
                "membership at connect time is what puts the connection in that room's group");
        }

        // And the personal group: a friend request is addressed to the user, not to a room.
        var personal = hub.Expect<JsonElement>(ChatHubEvents.FriendRequestReceived);
        await owner.Client.PostAsJsonAsync(
            "/api/Friends/requests", new { username = member.Username, message = (string?)null });
        (await personal.RawAsync()).ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact(DisplayName = RealtimeDeliverySpec.RoomMessageFanOut
                        + "a room message reaches every member's connection and nobody else's")]
    public async Task A_room_message_reaches_exactly_the_rooms_members()
    {
        var author = await CreateUserAsync();
        var member = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await author.CreateRoomWithAsync(member);

        await using var memberHub = await ConnectHubAsync(member);
        await using var outsiderHub = await ConnectHubAsync(outsider);

        var delivered = memberHub.Expect<MessageDto>(ChatHubEvents.MessageReceived);
        var notDelivered = outsiderHub.Expect<MessageDto>(
            ChatHubEvents.MessageReceived, timeout: TimeSpan.FromSeconds(2));

        var sent = await author.SendMessageAsync(room.Id, "for the members");

        var received = await delivered.ValueAsync();
        received.Id.Should().Be(sent.Id);
        received.Text.Should().Be("for the members");
        received.AuthorUserName.Should().Be(author.Username,
            "the payload carries enough to render without refetching the collection");

        (await notDelivered.TimedOutAsync()).Should().BeTrue(
            "someone outside the room is outside its group");
    }

    [Fact(DisplayName = RealtimeDeliverySpec.JoiningMidSession
                        + "a user who joins while already connected starts receiving that room's messages")]
    public async Task Joining_while_connected_takes_effect_without_reconnecting()
    {
        var owner = await CreateUserAsync();
        var joiner = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();

        // Connected first, and a member only afterwards — so enrolment cannot have happened at
        // connect time.
        await using var hub = await ConnectHubAsync(joiner);

        var beforeJoining = hub.Expect<MessageDto>(
            ChatHubEvents.MessageReceived, timeout: TimeSpan.FromSeconds(2));
        await owner.SendMessageAsync(room.Id, "said before they joined");
        (await beforeJoining.TimedOutAsync()).Should().BeTrue("they were not in the room yet");

        await joiner.JoinRoomAsync(room.Id);

        var afterJoining = hub.Expect<MessageDto>(ChatHubEvents.MessageReceived);
        var sent = await owner.SendMessageAsync(room.Id, "said after they joined");
        (await afterJoining.ValueAsync()).Id.Should().Be(sent.Id,
            "joining takes effect immediately for delivery, with no reconnect");
    }

    [Fact(DisplayName = RealtimeDeliverySpec.PersonalNotification
                        + "a personal notification reaches only the user it is addressed to")]
    public async Task A_personal_notification_goes_to_one_user_only()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();
        var bystander = await CreateUserAsync();

        await using var recipientHub = await ConnectHubAsync(recipient);
        await using var bystanderHub = await ConnectHubAsync(bystander);

        var addressed = recipientHub.Expect<JsonElement>(ChatHubEvents.FriendRequestReceived);
        var notAddressed = bystanderHub.Expect<JsonElement>(
            ChatHubEvents.FriendRequestReceived, timeout: TimeSpan.FromSeconds(2));

        await sender.Client.PostAsJsonAsync("/api/Friends/requests", new
        {
            username = recipient.Username,
            message = "let us be friends",
        });

        var payload = await addressed.RawAsync();
        payload.ToString().Should().Contain(sender.Username,
            "the notification names the requester so the list can render without refetching");

        (await notAddressed.TimedOutAsync()).Should().BeTrue(
            "a personal group has exactly one member");
    }

    [Fact(DisplayName = RealtimeDeliverySpec.MessageDelivery
                        + "a message reaches a connected recipient well within the three-second budget")]
    public async Task Delivery_is_inside_the_specified_budget()
    {
        var author = await CreateUserAsync();
        var recipient = await CreateUserAsync();
        var room = await author.CreateRoomWithAsync(recipient);

        await using var hub = await ConnectHubAsync(recipient);

        var expectation = hub.Expect<MessageDto>(ChatHubEvents.MessageReceived);
        var stopwatch = Stopwatch.StartNew();
        await author.SendMessageAsync(room.Id, "how long does this take?");
        await expectation.ValueAsync();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
            "the specification budgets three seconds from an accepted send to a connected recipient");
    }

    [Fact(DisplayName = RealtimeDeliverySpec.RefusedSend
                        + "a hub send the server refuses fails with an error carrying the reason")]
    public async Task A_refused_hub_send_fails_with_the_reason()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();

        await using var hub = await ConnectHubAsync(outsider);

        var invoking = async () => await hub.InvokeAsync(
            "SendMessage", room.Id, "let me in", (Guid?)null);

        var thrown = await invoking.Should().ThrowAsync<HubException>(
            "a refusal has to surface as an error rather than as silence");
        thrown.Which.Message.Should().Contain("not a member",
            "and the error carries the reason, so the client can say what went wrong");
    }

    [Fact(DisplayName = RealtimeDeliverySpec.RefusedSend
                        + "the hub enforces the same rules as the HTTP API, refusing an oversized send")]
    public async Task The_hub_enforces_the_same_rules_as_http()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();

        await using var hub = await ConnectHubAsync(author);

        var invoking = async () => await hub.InvokeAsync(
            "SendMessage", room.Id, new string('a', 3073), (Guid?)null);

        (await invoking.Should().ThrowAsync<HubException>()).Which.Message
            .Should().Contain("3 KB", "the size limit is the server's, whichever channel is used");
    }

    [Fact(DisplayName = RealtimeDeliverySpec.RoomMessageFanOut
                        + "a message sent over the hub fans out the same way one sent over HTTP does")]
    public async Task A_hub_send_fans_out_like_an_http_send()
    {
        var author = await CreateUserAsync();
        var recipient = await CreateUserAsync();
        var room = await author.CreateRoomWithAsync(recipient);

        await using var authorHub = await ConnectHubAsync(author);
        await using var recipientHub = await ConnectHubAsync(recipient);

        var toRecipient = recipientHub.Expect<MessageDto>(ChatHubEvents.MessageReceived);
        var toAuthor = authorHub.Expect<MessageDto>(ChatHubEvents.MessageReceived);

        await authorHub.InvokeAsync("SendMessage", room.Id, "over the socket", (Guid?)null);

        (await toRecipient.ValueAsync()).Text.Should().Be("over the socket");
        (await toAuthor.ValueAsync()).Text.Should().Be("over the socket",
            "the sender is in the room group too, which is why the client reconciles its own echo");
    }
}
