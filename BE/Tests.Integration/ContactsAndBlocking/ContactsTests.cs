using System.Net.Http.Json;
using Application.Features.Friends.Dtos;
using Domain.Enums;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.ContactsAndBlocking;

/// <summary>
/// Backend integration tests for the contact list, friend requests, and confirming or refusing them
/// — <c>openspec/specs/contacts-and-blocking/spec.md</c>.
/// </summary>
[Collection(Collections.Social)]
public class ContactsTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = ContactsAndBlockingSpec.ViewingContacts
                        + "the list carries confirmed friends and incoming pending requests, each distinguishable")]
    public async Task Contacts_carry_confirmed_friends_and_incoming_requests_separately()
    {
        var me = await CreateUserAsync();
        var confirmed = await CreateUserAsync();
        var asking = await CreateUserAsync();

        await me.BefriendAsync(confirmed);
        await RequestAsync(asking, me);

        var contacts = await ListContactsAsync(me);

        contacts.Should().HaveCount(2);

        var friend = contacts.Single(c => c.UserId == confirmed.UserId);
        friend.Status.Should().Be(FriendshipStatus.Accepted);
        friend.UserName.Should().Be(confirmed.Username);
        friend.AcceptedAt.Should().NotBeNull();

        var pending = contacts.Single(c => c.UserId == asking.UserId);
        pending.Status.Should().Be(FriendshipStatus.Pending,
            "the status is what separates the incoming-requests group from the confirmed ones");
        pending.AcceptedAt.Should().BeNull();
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.OutgoingRequestsAreNotListedAsContacts
                        + "an unanswered outgoing request does not appear in the sender's list")]
    public async Task An_outgoing_request_is_not_a_contact()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();

        await RequestAsync(sender, recipient);

        (await ListContactsAsync(sender)).Should().BeEmpty(
            "the sender has no contact until the request is answered");
        (await ListContactsAsync(recipient)).Should().ContainSingle(c => c.UserId == sender.UserId,
            "while the recipient does have something to answer");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.RequestByUsername
                        + "a request by username creates a pending friendship recorded against the sender")]
    public async Task A_request_by_username_creates_a_pending_friendship()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();

        var response = await RequestAsync(sender, recipient);

        response.IsSuccessStatusCode.Should().BeTrue();

        var friendship = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Friendships
                .SingleAsync(f =>
                    (f.UserAId == sender.UserId && f.UserBId == recipient.UserId) ||
                    (f.UserAId == recipient.UserId && f.UserBId == sender.UserId)));

        friendship.Status.Should().Be(FriendshipStatus.Pending);
        friendship.RequestedByUserId.Should().Be(sender.UserId,
            "who asked decides who may accept");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.RequestWithAMessage
                        + "the optional message is stored with the request")]
    public async Task The_optional_message_is_stored()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();

        await sender.Client.PostAsJsonAsync("/api/Friends/requests", new
        {
            username = recipient.Username,
            message = "we met at the conference",
        });

        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Friendships
                .SingleAsync(f => f.RequestedByUserId == sender.UserId));

        stored.Message.Should().Be("we met at the conference",
            "the notification delivers it, so it has to be persisted with the request");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.UnknownTarget
                        + "a request naming no account is rejected as user-not-found")]
    public async Task A_request_to_nobody_is_rejected()
    {
        var sender = await CreateUserAsync();

        var response = await sender.Client.PostAsJsonAsync("/api/Friends/requests", new
        {
            username = $"nobody{Guid.NewGuid():N}"[..20],
            message = (string?)null,
        });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("User.NotFoundByUsername");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.DuplicateRequest
                        + "a second request, or a request between existing friends, is rejected")]
    public async Task A_duplicate_request_is_rejected()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();

        (await RequestAsync(sender, recipient)).IsSuccessStatusCode.Should().BeTrue();

        var again = await RequestAsync(sender, recipient);
        again.IsSuccessStatusCode.Should().BeFalse();
        (await again.ReadErrorAsync())?.Code.Should().Be("Friendship.RequestAlreadyExists");

        var fromTheOtherSide = await RequestAsync(recipient, sender);
        fromTheOtherSide.IsSuccessStatusCode.Should().BeFalse(
            "a pending request in either direction is still a request between these two");

        await AcceptAsync(recipient, sender);
        var whenAlreadyFriends = await RequestAsync(sender, recipient);
        whenAlreadyFriends.IsSuccessStatusCode.Should().BeFalse();
        (await whenAlreadyFriends.ReadErrorAsync())?.Code.Should().Be("Friendship.AlreadyFriends");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.SelfRequest
                        + "a request to oneself is rejected")]
    public async Task A_request_to_oneself_is_rejected()
    {
        var user = await CreateUserAsync();

        var response = await RequestAsync(user, user);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Friendship.CannotRequestSelf");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.Accepting
                        + "accepting records the acceptance time and puts each user in the other's list")]
    public async Task Accepting_makes_the_friendship_mutual()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();
        await RequestAsync(sender, recipient);
        var before = DateTime.UtcNow.AddSeconds(-5);

        var response = await AcceptAsync(recipient, sender);

        response.IsSuccessStatusCode.Should().BeTrue();

        var forSender = (await ListContactsAsync(sender)).Single(c => c.UserId == recipient.UserId);
        var forRecipient = (await ListContactsAsync(recipient)).Single(c => c.UserId == sender.UserId);

        forSender.Status.Should().Be(FriendshipStatus.Accepted);
        forRecipient.Status.Should().Be(FriendshipStatus.Accepted);
        forSender.AcceptedAt.Should().NotBeNull();
        forSender.AcceptedAt!.Value.Should().BeAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.Rejecting
                        + "rejecting removes the request and leaves neither user in the other's list")]
    public async Task Rejecting_removes_the_request_entirely()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();
        await RequestAsync(sender, recipient);

        var response = await recipient.Client.PostAsync(
            $"/api/Friends/requests/{sender.UserId}/reject", content: null);

        response.IsSuccessStatusCode.Should().BeTrue();

        (await ListContactsAsync(sender)).Should().BeEmpty();
        (await ListContactsAsync(recipient)).Should().BeEmpty();

        var rows = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Friendships.CountAsync());
        rows.Should().Be(0, "a rejected request is removed rather than kept in a refused state");

        // And the pair can start over, which a lingering row would prevent.
        (await RequestAsync(sender, recipient)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.SenderCannotSelfAccept
                        + "the requesting user can neither accept nor reject their own request")]
    public async Task The_sender_can_neither_accept_nor_reject_their_own_request()
    {
        var sender = await CreateUserAsync();
        var recipient = await CreateUserAsync();
        await RequestAsync(sender, recipient);

        var selfAccept = await AcceptAsync(sender, recipient);
        selfAccept.IsSuccessStatusCode.Should().BeFalse();
        (await selfAccept.ReadErrorAsync())?.Code.Should().Be("Friendship.CannotAcceptOwnRequest");

        var selfReject = await sender.Client.PostAsync(
            $"/api/Friends/requests/{recipient.UserId}/reject", content: null);
        selfReject.IsSuccessStatusCode.Should().BeFalse();
        (await selfReject.ReadErrorAsync())?.Code.Should().Be("Friendship.CannotRejectOwnRequest");

        var stillPending = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Friendships.SingleAsync());
        stillPending.Status.Should().Be(FriendshipStatus.Pending, "nothing was decided");
    }

    [Fact(DisplayName = ContactsAndBlockingSpec.RemovingAFriend
                        + "removal deletes the friendship and empties it from both lists")]
    public async Task Removing_a_friend_ends_it_for_both()
    {
        var one = await CreateUserAsync();
        var other = await CreateUserAsync();
        await one.BefriendAsync(other);

        var response = await one.Client.DeleteAsync($"/api/Friends/{other.UserId}");

        response.IsSuccessStatusCode.Should().BeTrue();

        (await ListContactsAsync(one)).Should().BeEmpty();
        (await ListContactsAsync(other)).Should().BeEmpty("removal ends the friendship for both sides");

        var rows = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Friendships.CountAsync());
        rows.Should().Be(0);
    }

    private static Task<HttpResponseMessage> RequestAsync(TestUser sender, TestUser target) =>
        sender.Client.PostAsJsonAsync(
            "/api/Friends/requests", new { username = target.Username, message = (string?)null });

    private static Task<HttpResponseMessage> AcceptAsync(TestUser accepter, TestUser sender) =>
        accepter.Client.PostAsync($"/api/Friends/requests/{sender.UserId}/accept", content: null);

    private static async Task<List<FriendDto>> ListContactsAsync(TestUser user) =>
        (await user.Client.GetFromJsonAsync<List<FriendDto>>("/api/Friends"))!;
}
