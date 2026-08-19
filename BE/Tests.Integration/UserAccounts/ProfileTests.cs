using System.Net;
using System.Net.Http.Json;
using Application.Features.Friends.Dtos;
using Application.Features.Profile.Dtos;
using Application.Features.Rooms.Dtos;
using Domain.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.Data;
using Tests.Integration.Harness;

namespace Tests.Integration.UserAccounts;

/// <summary>
/// Backend integration tests for the profile half of
/// <c>openspec/specs/user-accounts/spec.md</c>: what a user can read about themselves, the one
/// identity field they can change, and what deleting the account does.
/// </summary>
[Collection(Collections.Identity)]
public class ProfileTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = UserAccountsSpec.ReadingOwnProfile
                        + "the profile carries id, username, display name, email, and creation time")]
    public async Task Own_profile_carries_every_specified_field()
    {
        var user = await CreateUserAsync();
        await SetDisplayNameAsync(user, "Ada Lovelace");

        var profile = await user.Client.GetFromJsonAsync<UserProfileDto>("/api/Me");

        profile.Should().NotBeNull();
        profile!.Id.Should().Be(user.UserId);
        profile.UserName.Should().Be(user.Username);
        profile.DisplayName.Should().Be("Ada Lovelace");
        profile.Email.Should().Be(user.Email);
        profile.CreatedAt.Should().NotBe(default);
    }

    [Fact(DisplayName = UserAccountsSpec.UnauthenticatedProfileRequest
                        + "a profile request with no credentials is rejected as unauthorized")]
    public async Task Profile_request_without_credentials_is_unauthorized()
    {
        var anonymous = App.CreateClient();

        var response = await anonymous.GetAsync("/api/Me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = UserAccountsSpec.NoDisplayName
                        + "a user with no display name still carries their username as the stable handle")]
    public async Task Without_a_display_name_the_username_is_what_is_carried()
    {
        var user = await CreateUserAsync();

        var profile = await user.Client.GetFromJsonAsync<UserProfileDto>("/api/Me");

        profile!.DisplayName.Should().BeNull("nothing has been set");
        profile.UserName.Should().Be(user.Username,
            "the username is what a client falls back to when there is no display name");
    }

    [Fact(DisplayName = UserAccountsSpec.SettingADisplayName
                        + "a saved display name reaches message headers, member lists, and contact lists")]
    public async Task A_saved_display_name_reaches_every_place_a_user_is_named()
    {
        var user = await CreateUserAsync();
        var friend = await CreateUserAsync();

        var room = await user.CreateRoomWithAsync(friend);
        await user.BefriendAsync(friend);

        await SetDisplayNameAsync(user, "Grace Hopper");

        // Message header
        var message = await user.SendMessageAsync(room.Id, "hello");
        message.AuthorDisplayName.Should().Be("Grace Hopper");
        message.AuthorUserName.Should().Be(user.Username, "the handle stays visible alongside it");

        // Member list
        var members = await friend.Client.GetFromJsonAsync<List<RoomMemberDto>>(
            $"/api/Rooms/{room.Id}/members");
        members!.Single(m => m.UserId == user.UserId).DisplayName.Should().Be("Grace Hopper");

        // Contact list
        var contacts = await friend.Client.GetFromJsonAsync<List<FriendDto>>("/api/Friends");
        contacts!.Single(c => c.UserId == user.UserId).DisplayName.Should().Be("Grace Hopper");
    }

    [Fact(Skip = "SPEC GAP user-accounts/display-name: the specification caps a display name at 50 "
                 + "characters; PATCH /api/Me/display-name stores any length. See docs/spec-gaps.md.",
          DisplayName = UserAccountsSpec.SettingADisplayName
                        + "a display name longer than 50 characters is refused")]
    public async Task A_display_name_over_the_limit_is_refused()
    {
        var user = await CreateUserAsync();

        var response = await user.Client.PatchAsJsonAsync(
            "/api/Me/display-name", new { displayName = new string('x', 51) });

        response.IsSuccessStatusCode.Should().BeFalse(
            "the specification caps a display name at 50 characters");

        var profile = await user.Client.GetFromJsonAsync<UserProfileDto>("/api/Me");
        profile!.DisplayName.Should().BeNull("the refused name was not stored");
    }

    [Fact(DisplayName = UserAccountsSpec.NoRenamePath
                        + "no exposed operation changes a username, and setting a display name leaves it alone")]
    public async Task Nothing_exposed_changes_a_username()
    {
        var user = await CreateUserAsync();

        // The only mutable identity field.
        await SetDisplayNameAsync(user, "Renamed In Display Only");

        var profile = await user.Client.GetFromJsonAsync<UserProfileDto>("/api/Me");
        profile!.UserName.Should().Be(user.Username, "the handle is fixed at creation");

        // And there is no route offering to change it. Reading the routing table is the only way to
        // assert the absence of an operation; probing a guessed URL would only prove that one guess
        // is not it.
        var mutating = new[] { "POST", "PUT", "PATCH", "DELETE" };
        var usernameRoutes = App.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.RoutePattern.RawText is { } route
                && route.Contains("username", StringComparison.OrdinalIgnoreCase)
                && (endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()
                        ?.HttpMethods.Any(method => mutating.Contains(method)) ?? false))
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        usernameRoutes.Should().BeEmpty(
            "a username is immutable, so no mutating route may name one as its subject");
    }

    [Fact(DisplayName = UserAccountsSpec.DeletingAnAccount
                        + "deletion marks the account deleted and removes the user's attachment files from storage")]
    public async Task Deleting_marks_the_account_and_clears_its_files_from_storage()
    {
        var user = await CreateUserAsync();
        var room = await user.CreateRoomAsync();
        await user.UploadAsync(room.Id, "notes.txt", "text/plain", "some content"u8.ToArray());

        var storagePath = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Attachments
                .Where(a => a.Message.AuthorId == user.UserId)
                .Select(a => a.StoragePath)
                .SingleAsync());

        File.Exists(Path.Combine(App.UploadsRoot, storagePath))
            .Should().BeTrue("the upload is on disk before the account goes away");

        var response = await user.Client.DeleteAsync("/api/Me");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stored = await App.WithScopeAsync(async services =>
            await services.GetRequiredService<ApplicationDbContext>().Users
                .SingleAsync(u => u.Id == user.UserId));
        stored.DeletedAt.Should().NotBeNull("deletion is a mark, not a row removal");

        File.Exists(Path.Combine(App.UploadsRoot, storagePath))
            .Should().BeFalse("the user's stored attachment content is removed with the account");
    }

    [Fact(DisplayName = UserAccountsSpec.DeletingTwice
                        + "a second delete of an already deleted account is refused as already deleted")]
    public async Task Deleting_an_already_deleted_account_is_refused()
    {
        var user = await CreateUserAsync();
        (await user.Client.DeleteAsync("/api/Me")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await user.Client.DeleteAsync("/api/Me");

        second.IsSuccessStatusCode.Should().BeFalse();
        (await second.ReadErrorAsync())?.Code.Should().Be("User.AlreadyDeleted");
    }

    private static async Task SetDisplayNameAsync(TestUser user, string displayName)
    {
        var response = await user.Client.PatchAsJsonAsync("/api/Me/display-name", new { displayName });
        response.IsSuccessStatusCode.Should().BeTrue(
            $"setting a display name failed: {await response.Content.ReadAsStringAsync()}");
    }
}
