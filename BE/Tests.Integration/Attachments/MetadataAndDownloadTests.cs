using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.Attachments;

/// <summary>
/// Backend integration tests for what is kept alongside the bytes and who may fetch them —
/// <c>openspec/specs/attachments/spec.md</c>, the <em>Attachment metadata</em>,
/// <em>Download access control</em>, and <em>Attachment storage and persistence</em> requirements.
///
/// Access is re-evaluated on every download rather than granted once at upload, which is the part
/// no client can be trusted with and the reason these live here.
/// </summary>
[Collection(Collections.Media)]
public class MetadataAndDownloadTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    private static readonly byte[] FileBytes = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x31, 0x2e, 0x37];

    [Fact(DisplayName = AttachmentsSpec.OriginalNamePreserved
                        + "an attachment keeps the name it was uploaded under, not its storage name")]
    public async Task The_original_file_name_is_preserved()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(
            room.Id, "Q3 results (final).pdf", "application/pdf", FileBytes);
        var attachment = message.Attachments.Single();

        attachment.FileName.Should().Be("Q3 results (final).pdf");

        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Attachments
                .SingleAsync(a => a.Id == attachment.Id));

        stored.FileName.Should().Be("Q3 results (final).pdf");
        stored.StoragePath.Should().NotContain("Q3 results",
            "lookup is keyed by the attachment id, so the original name is metadata rather than a path");

        var download = await uploader.Client.GetAsync($"/api/attachments/{attachment.Id}");
        download.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Q3 results (final).pdf",
            "and it comes back as the download name too");
    }

    [Fact(DisplayName = AttachmentsSpec.CommentOnAnAttachment
                        + "a comment supplied with an upload is stored on the attachment and becomes the message text")]
    public async Task A_comment_is_stored_and_becomes_the_message_text()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(
            room.Id, "chart.png", "image/png", FileBytes, comment: "the numbers we discussed");

        message.Text.Should().Be("the numbers we discussed");
        message.Attachments.Single().Comment.Should().Be("the numbers we discussed",
            "the comment is the attachment's caption as well as the message's text");
    }

    [Fact(DisplayName = AttachmentsSpec.CommentOnAnAttachment
                        + "an upload with no comment carries none, rather than an empty one")]
    public async Task An_upload_without_a_comment_has_no_comment()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(room.Id, "chart.png", "image/png", FileBytes);

        message.Text.Should().BeEmpty();
        message.Attachments.Single().Comment.Should().BeNull();
    }

    [Fact(DisplayName = AttachmentsSpec.MemberDownloads
                        + "a member gets the bytes back with the original name and content type")]
    public async Task A_member_downloads_the_file()
    {
        var uploader = await CreateUserAsync();
        var member = await CreateUserAsync();
        var room = await uploader.CreateRoomWithAsync(member);

        var message = await uploader.UploadAsync(
            room.Id, "shared.pdf", "application/pdf", FileBytes);
        var attachmentId = message.Attachments.Single().Id;

        var response = await member.Client.GetAsync($"/api/attachments/{attachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(FileBytes,
            "what comes back is what was put in, byte for byte");
    }

    [Fact(DisplayName = AttachmentsSpec.NonMemberDownloads
                        + "someone who is not a member of the containing chat is refused as not-a-member")]
    public async Task A_non_member_cannot_download()
    {
        var uploader = await CreateUserAsync();
        var outsider = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(room.Id, "private.pdf", "application/pdf", FileBytes);
        var attachmentId = message.Attachments.Single().Id;

        var response = await outsider.Client.GetAsync($"/api/attachments/{attachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.ReadErrorAsync())?.Code.Should().Be("Room.NotMember",
            "knowing the id is not access — membership is checked on the request, not at upload");
    }

    [Fact(DisplayName = AttachmentsSpec.UploaderWhoLostAccess
                        + "the uploader is refused their own file once they are banned from the room")]
    public async Task The_uploader_loses_access_when_banned()
    {
        var owner = await CreateUserAsync();
        var uploader = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(uploader);

        var message = await uploader.UploadAsync(room.Id, "mine.pdf", "application/pdf", FileBytes);
        var attachmentId = message.Attachments.Single().Id;

        (await uploader.Client.GetAsync($"/api/attachments/{attachmentId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "they can read it while they are a member");

        (await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{uploader.UserId}", new { reason = "spam" }))
            .IsSuccessStatusCode.Should().BeTrue();

        (await uploader.Client.GetAsync($"/api/attachments/{attachmentId}"))
            .IsSuccessStatusCode.Should().BeFalse(
                "having uploaded it is not standing access; the check is who is a member now");
    }

    [Fact(DisplayName = AttachmentsSpec.UploaderWhoLostAccess
                        + "the uploader is refused their own file once they leave the room")]
    public async Task The_uploader_loses_access_on_leaving()
    {
        var owner = await CreateUserAsync();
        var uploader = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(uploader);

        var message = await uploader.UploadAsync(room.Id, "mine.pdf", "application/pdf", FileBytes);
        var attachmentId = message.Attachments.Single().Id;

        (await uploader.Client.PostAsync($"/api/Rooms/{room.Id}/leave", content: null))
            .IsSuccessStatusCode.Should().BeTrue();

        (await uploader.Client.GetAsync($"/api/attachments/{attachmentId}"))
            .IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact(DisplayName = AttachmentsSpec.UnknownAttachment
                        + "an attachment id that does not exist returns not found")]
    public async Task An_unknown_attachment_id_is_not_found()
    {
        var user = await CreateUserAsync();

        var response = await user.Client.GetAsync($"/api/attachments/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = AttachmentsSpec.UnknownAttachment
                        + "an attachment whose stored content has gone returns not found rather than an empty file")]
    public async Task An_attachment_whose_content_is_missing_is_not_found()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(room.Id, "vanishing.pdf", "application/pdf", FileBytes);
        var attachmentId = message.Attachments.Single().Id;

        var storagePath = await App.WithScopeAsync(async services =>
            (await services.GetRequiredService<ApplicationDbContext>().Attachments
                .SingleAsync(a => a.Id == attachmentId)).StoragePath);
        File.Delete(Path.Combine(App.UploadsRoot, storagePath));

        (await uploader.Client.GetAsync($"/api/attachments/{attachmentId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound,
                "the record outliving its bytes is a not-found, not a zero-length download");
    }

    [Fact(DisplayName = AttachmentsSpec.StorageLayout
                        + "the bytes are written beneath the room's own directory in the configured uploads root")]
    public async Task The_file_is_written_under_the_rooms_directory()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();

        var message = await uploader.UploadAsync(room.Id, "report.pdf", "application/pdf", FileBytes);
        var attachmentId = message.Attachments.Single().Id;

        var storagePath = await App.WithScopeAsync(async services =>
            (await services.GetRequiredService<ApplicationDbContext>().Attachments
                .SingleAsync(a => a.Id == attachmentId)).StoragePath);

        var expected = Path.Combine(App.UploadsRoot, room.Id.ToString(), $"{attachmentId}.pdf");
        Path.Combine(App.UploadsRoot, storagePath).Should().Be(expected,
            "per-room directory, keyed by attachment id, under the configured root");
        File.Exists(expected).Should().BeTrue();
        (await File.ReadAllBytesAsync(expected)).Should().Equal(FileBytes);
    }

    [Fact(DisplayName = AttachmentsSpec.FileSurvivesLossOfAccess
                        + "the file stays in storage and stays readable by the room when the uploader loses access")]
    public async Task The_file_outlives_the_uploaders_access()
    {
        var owner = await CreateUserAsync();
        var uploader = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(uploader);

        var message = await uploader.UploadAsync(room.Id, "kept.pdf", "application/pdf", FileBytes);
        var attachmentId = message.Attachments.Single().Id;

        (await owner.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{uploader.UserId}"))
            .IsSuccessStatusCode.Should().BeTrue();

        var storagePath = await App.WithScopeAsync(async services =>
            (await services.GetRequiredService<ApplicationDbContext>().Attachments
                .SingleAsync(a => a.Id == attachmentId)).StoragePath);
        File.Exists(Path.Combine(App.UploadsRoot, storagePath)).Should().BeTrue(
            "losing access is not a deletion");

        var download = await owner.Client.GetAsync($"/api/attachments/{attachmentId}");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(FileBytes,
            "and the room's current members keep the file they were shared");
    }
}
