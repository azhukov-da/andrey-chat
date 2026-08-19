using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Common;
using Application.Features.Messages.Dtos;
using Application.Features.Rooms.Dtos;
using Domain.Enums;

namespace Tests.Integration.Harness;

/// <summary>
/// The arrangement steps every capability needs — make a room, put someone in it, post a message,
/// upload a file, make two accounts friends — expressed once, over the same HTTP endpoints a client
/// calls.
///
/// These are for <em>arranging</em>. Each throws on an unexpected status, because a failed
/// arrangement is a broken test rather than a finding. The behaviour actually under test is always
/// exercised by calling the endpoint directly in the test body and asserting on the response, so a
/// helper never stands between an assertion and the thing it is asserting about.
/// </summary>
public static class ChatApi
{
    /// <summary>
    /// Creates a room owned by <paramref name="user"/>. The default name is unique per call, so a
    /// test that does not care about naming never trips <c>chat-rooms/room-name-uniqueness</c>.
    /// </summary>
    public static async Task<RoomDto> CreateRoomAsync(
        this TestUser user,
        string? name = null,
        RoomVisibility visibility = RoomVisibility.Public,
        string? description = null)
    {
        var response = await user.Client.PostAsJsonAsync("/api/Rooms", new
        {
            name = name ?? UniqueName("room"),
            description,
            visibility,
        });

        return await ReadAsync<RoomDto>(response, "create room");
    }

    /// <summary>Joins <paramref name="user"/> to a public room.</summary>
    public static async Task JoinRoomAsync(this TestUser user, Guid roomId)
    {
        var response = await user.Client.PostAsync($"/api/Rooms/{roomId}/join", content: null);
        await EnsureSuccessAsync(response, "join room");
    }

    /// <summary>Creates a room and joins <paramref name="members"/> to it.</summary>
    public static async Task<RoomDto> CreateRoomWithAsync(
        this TestUser owner, params TestUser[] members)
    {
        var room = await owner.CreateRoomAsync();
        foreach (var member in members) await member.JoinRoomAsync(room.Id);
        return room;
    }

    public static async Task<MessageDto> SendMessageAsync(
        this TestUser user, Guid roomId, string text, Guid? replyToMessageId = null)
    {
        var response = await user.Client.PostAsJsonAsync(
            $"/api/rooms/{roomId}/Messages", new { text, replyToMessageId });

        return await ReadAsync<MessageDto>(response, "send message");
    }

    /// <summary>
    /// One page of history, newest first — the same shape the message list pages through, cursor
    /// and all.
    /// </summary>
    public static async Task<CursorPaged<MessageDto>> GetHistoryAsync(
        this TestUser user, Guid roomId, Guid? before = null, int limit = 50)
    {
        var query = $"/api/rooms/{roomId}/Messages?limit={limit}"
                    + (before is null ? string.Empty : $"&before={before}");

        var response = await user.Client.GetAsync(query);
        return await ReadAsync<CursorPaged<MessageDto>>(response, "read history");
    }

    /// <summary>
    /// Uploads an attachment. Returns the message the upload created, because that is what the
    /// endpoint returns and what the attachment is bound to.
    /// </summary>
    public static async Task<MessageDto> UploadAsync(
        this TestUser user,
        Guid roomId,
        string fileName,
        string contentType,
        byte[] content,
        string? comment = null)
    {
        var response = await user.PostUploadAsync(roomId, fileName, contentType, content, comment);
        return await ReadAsync<MessageDto>(response, "upload attachment");
    }

    /// <summary>
    /// The raw upload call, for the tests whose subject <em>is</em> the refusal — too large, not a
    /// member, banned, empty.
    /// </summary>
    public static Task<HttpResponseMessage> PostUploadAsync(
        this TestUser user,
        Guid roomId,
        string fileName,
        string contentType,
        byte[] content,
        string? comment = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(roomId.ToString()), "roomId" },
        };

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);

        if (comment is not null) form.Add(new StringContent(comment), "comment");

        return user.Client.PostAsync("/api/attachments/upload", form);
    }

    /// <summary>Sends a room invitation, which only an owner or admin may do.</summary>
    public static async Task<RoomInvitationDto> InviteAsync(
        this TestUser inviter, Guid roomId, TestUser invitee)
    {
        var response = await inviter.Client.PostAsJsonAsync(
            $"/api/Rooms/{roomId}/invitations", new { inviteeUsername = invitee.Username });

        return await ReadAsync<RoomInvitationDto>(response, "invite to room");
    }

    /// <summary>
    /// Makes two accounts confirmed friends: <paramref name="user"/> requests, <paramref name="other"/>
    /// accepts. The precondition for opening a direct chat.
    /// </summary>
    public static async Task BefriendAsync(this TestUser user, TestUser other)
    {
        var request = await user.Client.PostAsJsonAsync(
            "/api/Friends/requests", new { username = other.Username, message = (string?)null });
        await EnsureSuccessAsync(request, "send friend request");

        var accept = await other.Client.PostAsync(
            $"/api/Friends/requests/{user.UserId}/accept", content: null);
        await EnsureSuccessAsync(accept, "accept friend request");
    }

    /// <summary>Opens (or reopens) the direct chat between two accounts.</summary>
    public static async Task<RoomDto> OpenDirectChatAsync(this TestUser user, TestUser other)
    {
        var response = await user.Client.PostAsJsonAsync(
            "/api/DirectChats", new { username = other.Username });

        return await ReadAsync<RoomDto>(response, "open direct chat");
    }

    /// <summary>Bans <paramref name="target"/> from a room. Caller must be owner or admin.</summary>
    public static async Task BanAsync(
        this TestUser moderator, Guid roomId, TestUser target, string? reason = null)
    {
        var response = await moderator.Client.PostAsJsonAsync(
            $"/api/Rooms/{roomId}/bans/{target.UserId}", new { reason });
        await EnsureSuccessAsync(response, "ban member");
    }

    /// <summary>Promotes <paramref name="target"/> to admin. Caller must be the owner.</summary>
    public static async Task MakeAdminAsync(this TestUser owner, Guid roomId, TestUser target)
    {
        var response = await owner.Client.PostAsync(
            $"/api/Rooms/{roomId}/members/{target.UserId}/make-admin", content: null);
        await EnsureSuccessAsync(response, "make admin");
    }

    /// <summary>A name no other test will collide with.</summary>
    public static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..24];

    /// <summary>
    /// The <c>Application.Common.Error</c> body a refused call returns. Tests assert on
    /// <c>Code</c> rather than on the message, so rewording an error does not break them.
    /// </summary>
    public static async Task<Error?> ReadErrorAsync(this HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Error>();
        }
        catch (Exception)
        {
            // Not every refusal is shaped like an Error — Identity and the validation filter have
            // their own shapes. Those tests assert on the raw body instead.
            return null;
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string step)
    {
        await EnsureSuccessAsync(response, step);
        return await response.Content.ReadFromJsonAsync<T>()
               ?? throw new InvalidOperationException($"ChatApi {step} returned an empty body.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string step)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"ChatApi {step} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }
}
