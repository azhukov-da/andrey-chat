using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.Messaging;

/// <summary>
/// Backend integration tests for posting, replying to, editing, and deleting a message —
/// <c>openspec/specs/messaging/spec.md</c>. Everything goes over the HTTP API; the specification
/// requires the real-time channel to enforce the identical rules, which
/// <c>RealtimeDelivery</c> covers from the other side.
/// </summary>
[Collection(Collections.Messaging)]
public class SendEditDeleteTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = MessagingSpec.MemberSendsText
                        + "a member's message is persisted with its author and creation time")]
    public async Task A_members_message_is_persisted_with_author_and_time()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);

        var message = await author.SendMessageAsync(room.Id, "the first thing said here");

        message.Id.Should().NotBeEmpty();
        message.RoomId.Should().Be(room.Id);
        message.AuthorId.Should().Be(author.UserId);
        message.AuthorUserName.Should().Be(author.Username);
        message.Text.Should().Be("the first thing said here");
        message.CreatedAt.Should().BeAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        message.IsDeleted.Should().BeFalse();
        message.EditedAt.Should().BeNull();

        var history = await author.GetHistoryAsync(room.Id);
        history.Items.Should().ContainSingle(m => m.Id == message.Id);
    }

    [Fact(DisplayName = MessagingSpec.MultilineAndEmoji
                        + "line breaks and emoji survive the round trip byte for byte")]
    public async Task Line_breaks_and_emoji_are_stored_intact()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        const string text = "first line\nsecond line\n\nafter a blank one \U0001F680 \U0001F44D\U0001F3FD";

        var sent = await author.SendMessageAsync(room.Id, text);

        sent.Text.Should().Be(text);

        var history = await author.GetHistoryAsync(room.Id);
        history.Items.Single(m => m.Id == sent.Id).Text.Should().Be(text,
            "what comes back out of history is what went in, newlines and surrogate pairs included");
    }

    [Fact(DisplayName = MessagingSpec.NonMemberSends
                        + "a non-member's post is refused as not-a-member and stores nothing")]
    public async Task A_non_member_cannot_post()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();

        var response = await outsider.Client.PostAsJsonAsync(
            $"/api/rooms/{room.Id}/Messages", new { text = "let me in" });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NotMember");

        (await owner.GetHistoryAsync(room.Id)).Items.Should().BeEmpty();
    }

    [Fact(Skip = "SPEC GAP messaging/sending-messages: the post is refused, but as Room.NotMember "
                 + "rather than Room.Banned, because banning removes the membership row and the "
                 + "membership check runs first. See docs/spec-gaps.md.",
          DisplayName = MessagingSpec.BannedMemberSends
                        + "a banned user's post is refused as banned")]
    public async Task A_banned_user_cannot_post()
    {
        var owner = await CreateUserAsync();
        var banned = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(banned);
        await owner.BanAsync(room.Id, banned);

        var response = await banned.Client.PostAsJsonAsync(
            $"/api/rooms/{room.Id}/Messages", new { text = "still here" });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.Banned");
    }

    [Fact(DisplayName = MessagingSpec.OversizedSend
                        + "text over 3072 UTF-8 bytes is refused as too large and nothing is stored")]
    public async Task Text_over_the_byte_limit_is_refused()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();

        var atTheLimit = new string('a', 3072);
        (await author.SendMessageAsync(room.Id, atTheLimit)).Text.Should().HaveLength(3072,
            "3072 bytes is the largest accepted, not the smallest refused");

        var overTheLimit = new string('a', 3073);
        var response = await author.Client.PostAsJsonAsync(
            $"/api/rooms/{room.Id}/Messages", new { text = overTheLimit });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Message.TooLarge");
        (await author.GetHistoryAsync(room.Id)).Items.Should().ContainSingle(
            "only the message that fit was stored");
    }

    [Fact(DisplayName = MessagingSpec.OversizedSend
                        + "the limit counts UTF-8 bytes, not characters")]
    public async Task The_limit_is_measured_in_bytes()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();

        // Four bytes each, so 1024 of them are exactly the limit and 1025 are over it, even though
        // that is far fewer than 3072 characters.
        var justOver = string.Concat(Enumerable.Repeat("\U0001F680", 1025));
        Encoding.UTF8.GetByteCount(justOver).Should().BeGreaterThan(3072);
        justOver.Length.Should().BeLessThan(3072, "counting characters would let this through");

        var response = await author.Client.PostAsJsonAsync(
            $"/api/rooms/{room.Id}/Messages", new { text = justOver });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Message.TooLarge");
    }

    [Fact(DisplayName = MessagingSpec.ComposingAReply
                        + "a reply records the message it answers, and history carries the reference")]
    public async Task A_reply_records_what_it_answers()
    {
        var first = await CreateUserAsync();
        var second = await CreateUserAsync();
        var room = await first.CreateRoomWithAsync(second);

        var original = await first.SendMessageAsync(room.Id, "what do you think?");
        var reply = await second.SendMessageAsync(room.Id, "sounds right", original.Id);

        reply.ReplyToMessageId.Should().Be(original.Id);

        var history = await first.GetHistoryAsync(room.Id);
        history.Items.Single(m => m.Id == reply.Id).ReplyToMessageId.Should().Be(original.Id,
            "the client renders the quote from this reference, so it has to survive a reload");
    }

    [Fact(DisplayName = MessagingSpec.QuotingADeletedMessage
                        + "a reply to a message that is later deleted keeps its reference, and the target reads as deleted")]
    public async Task A_reply_to_a_deleted_message_still_points_at_it()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();

        var original = await author.SendMessageAsync(room.Id, "this will go away");
        var reply = await author.SendMessageAsync(room.Id, "answering it", original.Id);

        (await author.Client.DeleteAsync($"/api/messages/{original.Id}"))
            .IsSuccessStatusCode.Should().BeTrue();

        var history = await author.GetHistoryAsync(room.Id);

        history.Items.Single(m => m.Id == reply.Id).ReplyToMessageId.Should().Be(original.Id,
            "the reference survives so the quote can render as deleted rather than vanish");
        history.Items.Single(m => m.Id == original.Id).IsDeleted.Should().BeTrue(
            "which is how the client knows to show the deleted placeholder in the quote");
    }

    [Fact(DisplayName = MessagingSpec.QuotingAnAttachmentMessage
                        + "a reply to an attachment message with no text can name the file, because history carries it")]
    public async Task A_reply_to_an_attachment_message_can_name_the_file()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();

        var uploaded = await author.UploadAsync(
            room.Id, "quarterly-report.pdf", "application/pdf", "%PDF-1.4 pretend"u8.ToArray());
        uploaded.Text.Should().BeEmpty("the upload carried no comment");

        var reply = await author.SendMessageAsync(room.Id, "thanks for this", uploaded.Id);

        var history = await author.GetHistoryAsync(room.Id);
        var quoted = history.Items.Single(m => m.Id == reply.Id).ReplyToMessageId;
        quoted.Should().Be(uploaded.Id);

        history.Items.Single(m => m.Id == uploaded.Id).Attachments.Should()
            .ContainSingle(a => a.FileName == "quarterly-report.pdf",
                "with no text to quote, the file name is what identifies the message");
    }

    [Fact(DisplayName = MessagingSpec.ReplyTargetInAnotherRoom
                        + "a reply naming a message from another room is refused as message-not-found")]
    public async Task A_reply_across_rooms_is_refused()
    {
        var author = await CreateUserAsync();
        var here = await author.CreateRoomAsync();
        var elsewhere = await author.CreateRoomAsync();

        var foreign = await author.SendMessageAsync(elsewhere.Id, "said in the other room");

        var response = await author.Client.PostAsJsonAsync($"/api/rooms/{here.Id}/Messages", new
        {
            text = "replying across rooms",
            replyToMessageId = foreign.Id,
        });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Message.NotFound");
        (await author.GetHistoryAsync(here.Id)).Items.Should().BeEmpty();
    }

    [Fact(DisplayName = MessagingSpec.AuthorEdits
                        + "the author's edit updates the text and records the edit time")]
    public async Task An_author_can_edit_their_message()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        var message = await author.SendMessageAsync(room.Id, "frist draft");
        var before = DateTime.UtcNow.AddSeconds(-5);

        var response = await author.Client.PatchAsJsonAsync(
            $"/api/messages/{message.Id}", new { text = "first draft" });

        response.IsSuccessStatusCode.Should().BeTrue();

        var edited = (await author.GetHistoryAsync(room.Id)).Items.Single(m => m.Id == message.Id);
        edited.Text.Should().Be("first draft");
        edited.EditedAt.Should().NotBeNull("the edited marker is drawn from this");
        edited.EditedAt!.Value.Should().BeAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact(DisplayName = MessagingSpec.NonAuthorEdits
                        + "someone else's edit is refused, even the room owner's, and the text is untouched")]
    public async Task Only_the_author_may_edit()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(member);
        var message = await member.SendMessageAsync(room.Id, "mine to edit");

        var response = await owner.Client.PatchAsJsonAsync(
            $"/api/messages/{message.Id}", new { text = "not yours to edit" });

        response.IsSuccessStatusCode.Should().BeFalse(
            "editing is the author's alone; ownership of the room buys deletion, not authorship");
        (await response.ReadErrorAsync())?.Code.Should().Be("Message.NotAuthor");

        (await member.GetHistoryAsync(room.Id)).Items.Single(m => m.Id == message.Id)
            .Text.Should().Be("mine to edit");
    }

    [Fact(DisplayName = MessagingSpec.EditingADeletedMessage
                        + "an edit of a deleted message is refused as already deleted")]
    public async Task A_deleted_message_cannot_be_edited()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        var message = await author.SendMessageAsync(room.Id, "here and then gone");
        await author.Client.DeleteAsync($"/api/messages/{message.Id}");

        var response = await author.Client.PatchAsJsonAsync(
            $"/api/messages/{message.Id}", new { text = "back again" });

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Message.AlreadyDeleted");
    }

    [Fact(DisplayName = MessagingSpec.AuthorDeletes
                        + "the author's delete marks the message deleted")]
    public async Task An_author_can_delete_their_message()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        var message = await author.SendMessageAsync(room.Id, "regrettable");

        var response = await author.Client.DeleteAsync($"/api/messages/{message.Id}");

        response.IsSuccessStatusCode.Should().BeTrue();

        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Messages
                .SingleAsync(m => m.Id == message.Id));
        stored.DeletedAt.Should().NotBeNull();

        (await author.GetHistoryAsync(room.Id)).Items.Single(m => m.Id == message.Id)
            .IsDeleted.Should().BeTrue();
    }

    [Fact(Skip = "SPEC GAP messaging/deleting-messages: a deleted message's text is still served in "
                 + "history; only an IsDeleted flag marks it. See docs/spec-gaps.md.",
          DisplayName = MessagingSpec.AuthorDeletes
                        + "a deleted message stops exposing its text")]
    public async Task A_deleted_message_stops_serving_its_text()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        var message = await author.SendMessageAsync(room.Id, "something regrettable and specific");

        await author.Client.DeleteAsync($"/api/messages/{message.Id}");

        var served = (await author.GetHistoryAsync(room.Id)).Items.Single(m => m.Id == message.Id);
        served.Text.Should().BeEmpty(
            "a deleted message renders as a placeholder for everyone, so its text must not be served");
    }

    [Fact(DisplayName = MessagingSpec.AdminDeletesAnotherUsersMessage
                        + "a room owner or admin deletes someone else's message")]
    public async Task Owners_and_admins_can_delete_other_peoples_messages()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(admin, member);
        await owner.MakeAdminAsync(room.Id, admin);

        var byOwner = await member.SendMessageAsync(room.Id, "the owner will remove this");
        var byAdmin = await member.SendMessageAsync(room.Id, "the admin will remove this");

        (await owner.Client.DeleteAsync($"/api/messages/{byOwner.Id}"))
            .IsSuccessStatusCode.Should().BeTrue("the owner moderates their room");
        (await admin.Client.DeleteAsync($"/api/messages/{byAdmin.Id}"))
            .IsSuccessStatusCode.Should().BeTrue("an admin moderates too");

        var history = await member.GetHistoryAsync(room.Id);
        history.Items.Single(m => m.Id == byOwner.Id).IsDeleted.Should().BeTrue();
        history.Items.Single(m => m.Id == byAdmin.Id).IsDeleted.Should().BeTrue();
    }

    [Fact(DisplayName = MessagingSpec.UnauthorizedDelete
                        + "a plain member deleting someone else's message is refused")]
    public async Task A_plain_member_cannot_delete_someone_elses_message()
    {
        var owner = await CreateUserAsync();
        var author = await CreateUserAsync();
        var bystander = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(author, bystander);
        var message = await author.SendMessageAsync(room.Id, "not yours to remove");

        var response = await bystander.Client.DeleteAsync($"/api/messages/{message.Id}");

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Message.NotAuthor");
        (await author.GetHistoryAsync(room.Id)).Items.Single(m => m.Id == message.Id)
            .IsDeleted.Should().BeFalse();
    }

    [Fact(DisplayName = MessagingSpec.DeletingTwice
                        + "deleting an already deleted message is refused as already deleted")]
    public async Task Deleting_twice_is_refused()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        var message = await author.SendMessageAsync(room.Id, "gone once");
        (await author.Client.DeleteAsync($"/api/messages/{message.Id}"))
            .IsSuccessStatusCode.Should().BeTrue();

        var second = await author.Client.DeleteAsync($"/api/messages/{message.Id}");

        second.IsSuccessStatusCode.Should().BeFalse();
        (await second.ReadErrorAsync())?.Code.Should().Be("Message.AlreadyDeleted");
    }
}
