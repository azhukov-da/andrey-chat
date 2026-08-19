using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Common;
using Application.Features.Messages.Dtos;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.Messaging;

/// <summary>
/// Backend integration tests for reading history back — <c>openspec/specs/messaging/spec.md</c>.
///
/// The API returns a page newest-first with a cursor, which is what the specification asks paging to
/// be built on; presenting that page oldest-to-newest is the client's job and is verified at the
/// frontend layer. What matters here is that the ordering is total and stable, that the cursor walks
/// backwards without gaps or repeats, and that the end of history is reported.
/// </summary>
[Collection(Collections.Messaging)]
public class HistoryTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = MessagingSpec.ReadingHistory
                        + "a page carries the most recent messages in a total order, with author, time, and attachments")]
    public async Task History_carries_the_recent_messages_with_everything_needed_to_render_them()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();

        var sent = new List<MessageDto>();
        for (var i = 1; i <= 5; i++) sent.Add(await author.SendMessageAsync(room.Id, $"message {i}"));
        var withFile = await author.UploadAsync(
            room.Id, "diagram.png", "image/png", "not really a png"u8.ToArray(), "have a look");

        var page = await author.GetHistoryAsync(room.Id);

        page.Items.Select(m => m.Id).Should().Equal(
            new[] { withFile.Id }.Concat(sent.AsEnumerable().Reverse().Select(m => m.Id)),
            "the page is newest-first and totally ordered, so the client can reverse it for display");

        var first = page.Items.Last();
        first.Text.Should().Be("message 1");
        first.AuthorUserName.Should().Be(author.Username);
        first.CreatedAt.Should().NotBe(default);

        page.Items.Single(m => m.Id == withFile.Id).Attachments.Should()
            .ContainSingle(a => a.FileName == "diagram.png" && a.Comment == "have a look",
                "attachments come with the message rather than needing a second call");
    }

    [Fact(DisplayName = MessagingSpec.OfflineRecipient
                        + "messages sent while a member was away are in their history when they next read it")]
    public async Task Messages_sent_while_away_are_waiting_in_history()
    {
        var talker = await CreateUserAsync();
        var away = await CreateUserAsync();
        var room = await talker.CreateRoomWithAsync(away);

        // The away user has no connection and makes no request at all while these are sent.
        var whileAway = new List<Guid>();
        for (var i = 1; i <= 3; i++)
            whileAway.Add((await talker.SendMessageAsync(room.Id, $"while you were out {i}")).Id);

        var onReturn = await away.GetHistoryAsync(room.Id);

        onReturn.Items.Select(m => m.Id).Should().Contain(whileAway,
            "history is the delivery mechanism for anyone who was not connected");
    }

    [Fact(DisplayName = MessagingSpec.HistoryAcrossRestarts
                        + "messages survive a restart of the application")]
    public async Task History_survives_an_application_restart()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        var message = await author.SendMessageAsync(room.Id, "written before the restart");

        // A second host over the same database is what a restart is, from the data's point of view:
        // a fresh process, fresh caches, fresh DI graph, same rows.
        await using var restarted = await ChatAppFactory.CreateAsync(Fixture.ConnectionString);
        var client = restarted.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", author.AccessToken);

        var page = await client.GetFromJsonAsync<CursorPaged<MessageDto>>(
            $"/api/rooms/{room.Id}/Messages?limit=50");

        page!.Items.Should().ContainSingle(m => m.Id == message.Id && m.Text == "written before the restart");
    }

    [Fact(DisplayName = MessagingSpec.ScrollingBack
                        + "the cursor walks back through older pages without gaps or repeats")]
    public async Task Paging_backwards_covers_the_history_exactly_once()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();

        var everything = new List<Guid>();
        for (var i = 1; i <= 25; i++)
            everything.Add((await author.SendMessageAsync(room.Id, $"message {i}")).Id);

        var seen = new List<Guid>();
        var page = await author.GetHistoryAsync(room.Id, limit: 10);
        seen.AddRange(page.Items.Select(m => m.Id));
        page.HasMore.Should().BeTrue("15 messages are still older than this page");

        while (page.HasMore)
        {
            page = await author.GetHistoryAsync(room.Id, before: seen.Last(), limit: 10);
            seen.AddRange(page.Items.Select(m => m.Id));
        }

        seen.Should().OnlyHaveUniqueItems("a page boundary must not repeat a message");
        seen.Should().BeEquivalentTo(everything, "and must not skip one either");
        seen.Should().Equal(everything.AsEnumerable().Reverse(),
            "the walk backwards is in reverse chronological order throughout");
    }

    [Fact(DisplayName = MessagingSpec.ReachingTheBeginning
                        + "the page that reaches the start of history reports that nothing older remains")]
    public async Task Reaching_the_start_of_history_is_reported()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();
        for (var i = 1; i <= 3; i++) await author.SendMessageAsync(room.Id, $"message {i}");

        var page = await author.GetHistoryAsync(room.Id, limit: 10);

        page.Items.Should().HaveCount(3);
        page.HasMore.Should().BeFalse("everything there is fits in this page");
        page.NextCursor.Should().BeNull("there is nothing for a next page to start from");

        var older = await author.GetHistoryAsync(room.Id, before: page.Items.Last().Id, limit: 10);
        older.Items.Should().BeEmpty("nothing precedes the oldest message");
        older.HasMore.Should().BeFalse();
    }

    [Fact(DisplayName = MessagingSpec.ReachingTheBeginning
                        + "an empty chat reports no messages and no more to load")]
    public async Task An_empty_chat_reports_an_empty_history()
    {
        var author = await CreateUserAsync();
        var room = await author.CreateRoomAsync();

        var page = await author.GetHistoryAsync(room.Id);

        page.Items.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
    }

    [Fact(DisplayName = MessagingSpec.NonMemberRequestsHistory
                        + "a non-member's history request is refused as not-a-member")]
    public async Task A_non_member_cannot_read_history()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();
        await owner.SendMessageAsync(room.Id, "members only");

        var response = await outsider.Client.GetAsync($"/api/rooms/{room.Id}/Messages");

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NotMember");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("members only",
            "a refusal must not leak the content it is refusing");
    }
}
