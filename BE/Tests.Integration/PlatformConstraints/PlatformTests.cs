using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;

namespace Tests.Integration.PlatformConstraints;

/// <summary>
/// Backend integration tests for the platform's own guarantees —
/// <c>openspec/specs/platform-constraints/spec.md</c>.
///
/// Three of these restart or rebuild the application host, which is the only way to tell state that
/// lives in the database from state that lives in a process. The capacity scenarios and the
/// very-large-history one are capacity claims and belong to the load layer, which this change does
/// not build; the proxy boundary and the deployment topology are only visible from a browser against
/// the compose stack, so they belong to the end-to-end layer. Both omissions are recorded in
/// <c>docs/test-layer-triage.md</c>.
/// </summary>
[Collection(Collections.Platform)]
public class PlatformTests(ChatAppFixture fixture) : IntegrationTest(fixture)
{
    [Fact(DisplayName = PlatformConstraintsSpec.Restart
                        + "messages, memberships, roles, bans and invitations are all intact after the host restarts")]
    public async Task State_survives_a_restart_of_the_application_host()
    {
        var owner = await CreateUserAsync();
        var admin = await CreateUserAsync();
        var member = await CreateUserAsync();
        var banned = await CreateUserAsync();
        var invitee = await CreateUserAsync();

        var room = await owner.CreateRoomWithAsync(admin, member, banned);
        var message = await owner.SendMessageAsync(room.Id, "written before the restart");
        await owner.MakeAdminAsync(room.Id, admin);
        await owner.InviteAsync(room.Id, invitee);
        (await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{banned.UserId}", new { reason = "spam" }))
            .IsSuccessStatusCode.Should().BeTrue();

        // A second host on the same database is what a restart looks like from the outside: a new
        // process, nothing carried over in memory, everything that mattered read back from Postgres.
        await using var restarted = await ChatAppFactory.CreateAsync(Fixture.ConnectionString);

        var afterRestart = await restarted.WithScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            return new
            {
                Messages = await context.Messages.CountAsync(m => m.RoomId == room.Id),
                MessageText = await context.Messages.Where(m => m.Id == message.Id)
                    .Select(m => m.Text).SingleAsync(),
                Memberships = await context.RoomMemberships.CountAsync(m => m.RoomId == room.Id),
                AdminRole = await context.RoomMemberships
                    .Where(m => m.RoomId == room.Id && m.UserId == admin.UserId)
                    .Select(m => m.Role).SingleAsync(),
                Bans = await context.RoomBans.CountAsync(b => b.RoomId == room.Id),
                Invitations = await context.RoomInvitations.CountAsync(i => i.RoomId == room.Id),
            };
        });

        afterRestart.Messages.Should().Be(1);
        afterRestart.MessageText.Should().Be("written before the restart");
        afterRestart.Memberships.Should().Be(3, "the banned member's membership was removed by the ban");
        afterRestart.AdminRole.Should().Be(Domain.Enums.RoomRole.Admin);
        afterRestart.Bans.Should().Be(1);
        afterRestart.Invitations.Should().Be(1);
    }

    [Fact(DisplayName = PlatformConstraintsSpec.Restart
                        + "a signed-in user's data is still reachable over HTTP through a restarted host")]
    public async Task History_is_readable_through_a_restarted_host()
    {
        var owner = await CreateUserAsync();
        var room = await owner.CreateRoomAsync();
        await owner.SendMessageAsync(room.Id, "still here afterwards");

        await using var restarted = await ChatAppFactory.CreateAsync(Fixture.ConnectionString);
        var throughTheNewHost = await AuthHelper.SignInAgainAsync(restarted, owner);

        var history = (await throughTheNewHost.GetHistoryAsync(room.Id)).Items;

        history.Should().ContainSingle().Which.Text.Should().Be("still here afterwards",
            "the account, the room, and its history all outlive the process that created them");
    }

    [Fact(DisplayName = PlatformConstraintsSpec.StartupMigration
                        + "starting against a database behind the current schema migrates it before serving traffic")]
    public async Task Starting_against_an_unmigrated_database_migrates_it()
    {
        // An empty database is as far behind the current schema as a database can be.
        await using var blank = await TestDatabase.CreateAsync();

        await using var host = await ChatAppFactory.CreateAsync(blank.ConnectionString);

        var schema = await host.WithScopeAsync(async services =>
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            return new
            {
                Applied = (await context.Database.GetAppliedMigrationsAsync()).Count(),
                Pending = (await context.Database.GetPendingMigrationsAsync()).Count(),
            };
        });

        schema.Applied.Should().BeGreaterThan(0, "startup applied the migrations itself");
        schema.Pending.Should().Be(0, "and left nothing outstanding");

        // And it is serving: registration writes through the schema that was just created.
        var user = await AuthHelper.CreateUserAsync(host, "Migration test device", "IntegrationTests/1.0");
        (await user.Client.GetAsync("/api/Me")).StatusCode.Should().Be(HttpStatusCode.OK,
            "traffic is served against the migrated schema, not against a half-built one");
    }

    [Fact(DisplayName = PlatformConstraintsSpec.ConfiguredRoot
                        + "uploaded content is written beneath the configured uploads root and served back from there")]
    public async Task Uploads_are_written_beneath_the_configured_root()
    {
        var uploader = await CreateUserAsync();
        var room = await uploader.CreateRoomAsync();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        var message = await uploader.UploadAsync(room.Id, "under-the-root.pdf", "application/pdf", bytes);
        var attachmentId = message.Attachments.Single().Id;

        var storagePath = await App.WithScopeAsync(async services =>
            (await services.GetRequiredService<ApplicationDbContext>().Attachments
                .SingleAsync(a => a.Id == attachmentId)).StoragePath);

        Path.IsPathRooted(storagePath).Should().BeFalse(
            "what is persisted is a path relative to the root, so moving the root moves the content");

        var onDisk = Path.Combine(App.UploadsRoot, storagePath);
        File.Exists(onDisk).Should().BeTrue($"nothing was written at {onDisk}");
        (await File.ReadAllBytesAsync(onDisk)).Should().Equal(bytes);

        var download = await uploader.Client.GetAsync($"/api/attachments/{attachmentId}");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes,
            "and the download is served from that same file, not from the database");
    }

    [Fact(DisplayName = PlatformConstraintsSpec.BanTakesEffectImmediately
                        + "the very next read, post, and download after a ban are all refused")]
    public async Task A_ban_binds_on_the_next_call_of_every_kind()
    {
        var owner = await CreateUserAsync();
        var target = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(target);

        var upload = await target.UploadAsync(room.Id, "theirs.pdf", "application/pdf", [1, 2, 3]);
        var attachmentId = upload.Attachments.Single().Id;

        // Everything works right up to the moment of the ban — the point is the next call, not the
        // one after a cache expires.
        (await target.Client.GetAsync($"/api/rooms/{room.Id}/Messages")).IsSuccessStatusCode
            .Should().BeTrue();

        (await owner.Client.PostAsJsonAsync(
            $"/api/Rooms/{room.Id}/bans/{target.UserId}", new { reason = "spam" }))
            .IsSuccessStatusCode.Should().BeTrue();

        (await target.Client.GetAsync($"/api/rooms/{room.Id}/Messages")).IsSuccessStatusCode
            .Should().BeFalse("reading history is refused from the next request on");
        (await target.Client.PostAsJsonAsync(
            $"/api/rooms/{room.Id}/Messages", new { text = "let me back in" }))
            .IsSuccessStatusCode.Should().BeFalse("so is posting");
        (await target.Client.GetAsync($"/api/attachments/{attachmentId}")).IsSuccessStatusCode
            .Should().BeFalse("and so is fetching a file they uploaded themselves");
    }

    [Fact(DisplayName = PlatformConstraintsSpec.PromotionTakesEffectImmediately
                        + "the very next moderation call after a promotion is authorized")]
    public async Task A_promotion_binds_on_the_next_call()
    {
        var owner = await CreateUserAsync();
        var promoted = await CreateUserAsync();
        var newcomer = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(promoted, newcomer);

        (await promoted.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{newcomer.UserId}"))
            .IsSuccessStatusCode.Should().BeFalse("a plain member may not moderate");

        await owner.MakeAdminAsync(room.Id, promoted);

        (await promoted.Client.DeleteAsync($"/api/Rooms/{room.Id}/members/{newcomer.UserId}"))
            .IsSuccessStatusCode.Should().BeTrue(
                "the new role is read from the database on the request, with nothing to wait for");
    }

    [Fact(DisplayName = PlatformConstraintsSpec.PromotionTakesEffectImmediately
                        + "a promotion is visible to a session that was already signed in, with no reconnect")]
    public async Task A_promotion_reaches_an_already_open_session()
    {
        var owner = await CreateUserAsync();
        var promoted = await CreateUserAsync();
        var room = await owner.CreateRoomWithAsync(promoted);

        // The client was authenticated before the promotion, so its token cannot carry the new role.
        await owner.MakeAdminAsync(room.Id, promoted);

        (await promoted.Client.GetAsync($"/api/Rooms/{room.Id}/bans")).IsSuccessStatusCode
            .Should().BeTrue(
                "authority comes from the persisted membership, not from a claim minted at sign-in");
    }
}
