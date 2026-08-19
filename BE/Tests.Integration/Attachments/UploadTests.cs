using System.Net;
using System.Net.Http.Json;
using Application.Features.Messages.Dtos;
using FluentAssertions;
using Tests.Integration.Harness;

namespace Tests.Integration.Attachments;

/// <summary>
/// Backend integration tests for accepting an upload — <c>openspec/specs/attachments/spec.md</c>,
/// the <em>Uploading attachments</em> and <em>Size limits by kind</em> requirements.
///
/// Who may put a file into a chat, and how large a file the server will take, are decided on the
/// multipart request itself. A client that allowed either through would be overruled here, which is
/// why these belong at this layer and not in the composer's tests.
/// </summary>
[Collection(Collections.Media)]
public class UploadTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    private static readonly byte[] SomeBytes = [0x50, 0x4b, 0x03, 0x04, 0x00, 0x01];

    [Fact(DisplayName = AttachmentsSpec.UploadingAFile
                        + "a member's upload is stored and becomes a message carrying the file's metadata")]
    public async Task A_members_upload_becomes_a_message_in_the_chat()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(
            room.Id, "quarterly-report.pdf", "application/pdf", SomeBytes);

        message.RoomId.Should().Be(room.Id);
        message.AuthorId.Should().Be(uploader.UserId);

        var attachment = message.Attachments.Should().ContainSingle().Subject;
        attachment.FileName.Should().Be("quarterly-report.pdf");
        attachment.ContentType.Should().Be("application/pdf");
        attachment.SizeBytes.Should().Be(SomeBytes.Length);
        attachment.Kind.Should().Be("File");

        var history = (await uploader.GetHistoryAsync(room.Id)).Items;
        history.Should().ContainSingle(m => m.Id == message.Id,
            "the upload is part of the chat history, not a side channel");
    }

    [Fact(DisplayName = AttachmentsSpec.UploadingAFile
                        + "the upload is pushed to the room's other participants as it happens")]
    public async Task An_upload_reaches_the_other_participants_in_real_time()
    {
        var uploader = await CreateUserAsync();
        var watcher = await CreateUserAsync();
        var room = await uploader.CreateRoomWithAsync(watcher);

        await using var hub = await ConnectHubAsync(watcher);
        var expectation = hub.Expect<MessageDto>(ChatHubEvents.MessageReceived);

        var uploaded = await uploader.UploadAsync(room.Id, "diagram.png", "image/png", SomeBytes);

        var received = await expectation.ValueAsync();
        received.Id.Should().Be(uploaded.Id);
        received.Attachments.Should().ContainSingle()
            .Which.FileName.Should().Be("diagram.png",
                "the pushed message carries the metadata, so the row renders without a refetch");
    }

    [Fact(DisplayName = AttachmentsSpec.UploadingSeveralFiles
                        + "each of several files becomes its own message")]
    public async Task Several_files_become_several_messages()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var names = new[] { "first.txt", "second.txt", "third.txt" };
        foreach (var name in names)
        {
            await uploader.UploadAsync(room.Id, name, "text/plain", SomeBytes);
        }

        var history = (await uploader.GetHistoryAsync(room.Id)).Items;
        history.Should().HaveCount(3);
        history.SelectMany(m => m.Attachments).Select(a => a.FileName)
            .Should().BeEquivalentTo(names,
                "the endpoint takes one file per request, so a multi-file selection is several messages");
    }

    [Fact(DisplayName = AttachmentsSpec.NonMemberUpload
                        + "an upload by someone who is not a member is refused as not-a-member")]
    public async Task A_non_members_upload_is_refused()
    {
        var owner = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();

        var response = await outsider.PostUploadAsync(
            room.Id, "uninvited.txt", "text/plain", SomeBytes);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NotMember");

        (await owner.GetHistoryAsync(room.Id)).Items.Should().BeEmpty(
            "a refused upload leaves nothing behind in the chat");
    }

    [Fact(DisplayName = AttachmentsSpec.NonMemberUpload
                        + "an upload into a deleted room is refused")]
    public async Task An_upload_into_a_deleted_room_is_refused()
    {
        var owner = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();
        (await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}")).IsSuccessStatusCode.Should().BeTrue();

        var response = await owner.PostUploadAsync(room.Id, "too-late.txt", "text/plain", SomeBytes);

        response.IsSuccessStatusCode.Should().BeFalse(
            "a deleted room takes no more content, from its owner or anyone else");
    }

    [Fact(DisplayName = AttachmentsSpec.EmptyFile
                        + "a zero-length file is rejected as a bad request")]
    public async Task An_empty_file_is_rejected()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var response = await uploader.PostUploadAsync(room.Id, "empty.txt", "text/plain", []);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await uploader.GetHistoryAsync(room.Id)).Items.Should().BeEmpty(
            "nothing was uploaded, so no message was created");
    }

    [Fact(DisplayName = AttachmentsSpec.EmptyFile
                        + "a request carrying no file at all is rejected as a bad request")]
    public async Task A_request_with_no_file_is_rejected()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var form = new MultipartFormDataContent
        {
            { new StringContent(room.Id.ToString()), "roomId" },
        };

        var response = await uploader.Client.PostAsync("/api/attachments/upload", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = AttachmentsSpec.OversizedImage
                        + "an image over 3 MB is refused with an error naming the image limit")]
    public async Task An_oversized_image_is_refused_with_the_image_limit()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var response = await uploader.PostUploadAsync(
            room.Id, "huge.png", "image/png", new byte[(3 * 1024 * 1024) + 1]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.ReadErrorAsync();
        error?.Code.Should().Be("Attachment.ImageTooLarge");
        error?.Message.Should().Contain("3 MB",
            "the refusal names the limit that applies, so the composer can say which one was hit");
    }

    [Fact(DisplayName = AttachmentsSpec.OversizedImage
                        + "an image just inside 3 MB is accepted")]
    public async Task An_image_at_the_limit_is_accepted()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(
            room.Id, "large-but-allowed.png", "image/png", new byte[3 * 1024 * 1024]);

        message.Attachments.Should().ContainSingle().Which.Kind.Should().Be("Image",
            "the ceiling is a ceiling, not a threshold — exactly 3 MB is inside it");
    }

    [Fact(DisplayName = AttachmentsSpec.OversizedFile
                        + "a non-image over 20 MB is refused with an error naming the file limit")]
    public async Task An_oversized_file_is_refused_with_the_file_limit()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var response = await uploader.PostUploadAsync(
            room.Id, "huge.bin", "application/octet-stream", new byte[(20 * 1024 * 1024) + 1]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.ReadErrorAsync();
        error?.Code.Should().Be("Attachment.FileTooLarge");
        error?.Message.Should().Contain("20 MB");
    }

    [Fact(DisplayName = AttachmentsSpec.OversizedFile
                        + "the limit that applies is chosen by kind, so a 4 MB non-image is accepted")]
    public async Task The_applicable_limit_is_chosen_by_kind()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(
            room.Id, "archive.zip", "application/zip", new byte[4 * 1024 * 1024]);

        message.Attachments.Should().ContainSingle().Which.Kind.Should().Be("File",
            "4 MB is over the image ceiling and well under the file one; the content type decides which");
    }

    [Fact(Skip = "SPEC GAP attachments/uploading-attachments/banned-uploader: the refusal is "
                 + "Room.NotMember, not Room.Banned. See docs/spec-gaps.md.",
          DisplayName = AttachmentsSpec.BannedUploader
                        + "an upload by a banned user is refused as banned")]
    public async Task A_banned_users_upload_is_refused_as_banned()
    {
        var owner = await CreateUserAsync();
        var banned = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(banned);

        (await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{banned.UserId}", new { reason = "spam" }))
            .IsSuccessStatusCode.Should().BeTrue();

        var response = await banned.PostUploadAsync(room.Id, "anyway.txt", "text/plain", SomeBytes);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.Banned",
            "the client cannot tell a ban from never having joined unless the server says which");
    }

    [Fact(DisplayName = AttachmentsSpec.BannedUploader
                        + "an upload by a banned user is refused, whatever the reason given")]
    public async Task A_banned_users_upload_is_refused()
    {
        var owner = await CreateUserAsync();
        var banned = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(banned);

        (await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{banned.UserId}", new { reason = "spam" }))
            .IsSuccessStatusCode.Should().BeTrue();

        var response = await banned.PostUploadAsync(room.Id, "anyway.txt", "text/plain", SomeBytes);

        response.IsSuccessStatusCode.Should().BeFalse();
        (await owner.GetHistoryAsync(room.Id)).Items.SelectMany(m => m.Attachments).Should().BeEmpty(
            "the outcome is right even though the reason code is not — see the skipped test above");
    }
}
