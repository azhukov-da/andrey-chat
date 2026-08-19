# Test Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the three gated test layers, the coverage gates, and the spec-traceability tooling, and prove all of it end to end on the `user-sessions` capability.

**Architecture:** Backend integration tests host the ASP.NET app in-process with `WebApplicationFactory<Program>` against a throwaway PostgreSQL database on the existing compose server; frontend tests run under vitest + jsdom with MSW standing in for HTTP and a fake transport standing in for SignalR; end-to-end tests drive the real compose stack through Playwright. Every test declares which spec scenarios it verifies by embedding `@spec:<id>` markers in its display name, and a dependency-free Node tool cross-references those claims against the scenarios parsed out of `openspec/specs/`.

**Tech Stack:** .NET 10, xUnit v3, `Microsoft.AspNetCore.Mvc.Testing`, Respawn, Npgsql, coverlet + ReportGenerator, SignalR .NET client; React 19, vitest 4, jsdom, MSW 2, Testing Library; Playwright; Node 20+ (ESM, no dependencies) for the tooling.

**Spec:** `openspec/changes/add-test-harness/` — read `proposal.md`, `specs/automated-testing/spec.md`, and `design.md` before starting. `design.md` decisions D1–D10 are referenced by number throughout this plan.

## Global Constraints

- **Scenario identifier format:** `<capability>/<requirement-slug>/<scenario-slug>`. Slugs are the heading text lowercased, every run of non-alphanumeric characters collapsed to a single `-`, leading and trailing `-` trimmed. Example: `user-sessions/revoking-sessions/revoking-a-foreign-session`.
- **Claim marker format:** `@spec:<identifier>` embedded in the test's display name. A test may carry more than one, space-separated. (design D5)
- **Coverage threshold:** 80% lines, backend and frontend measured separately. Configured everywhere, but reported **informationally** by `test.bat` in this change — it becomes enforcing only when the last per-capability change lands. (design D7, proposal Non-Goals)
- **Test database naming:** `chat_test_{guid:N}` on `Host=localhost;Port=55432`. Nothing in the test suite may open a connection to the `chat` database. (design D1, Risks)
- **The database port is published by `docker-compose.tests.yml` only.** `docker-compose.yml` must not gain a `ports` entry for `db` — `platform-constraints/deployment-topology/bringing-the-stack-up` requires the database to be unreachable from outside the internal network, so publishing it in the base file would break a spec this harness exists to verify. Bring the database up for tests with `docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db`. (design D1)
- **No `Task.Delay` / `page.waitForTimeout` / `sleep` anywhere.** Real-time assertions use `TaskCompletionSource` with a bounded timeout on the backend, and Playwright's auto-waiting locators on the frontend. (design D3)
- **This change adds tests only.** The sole production-code edits permitted are the `Program.cs` partial-class marker and the `docker-compose.yml` port. If a test reveals that production behavior contradicts the spec, **do not fix it here** — record it in a `## Findings` section at the bottom of `docs/test-coverage/scenarios.md` and keep the test asserting the spec'd behavior in a `Skip`ped state so it is visible.
- **Password used by all test fixtures:** `Passw0rd` — satisfies the Identity policy in `BE/Infrastructure/DependencyInjection.cs` (digit, lowercase, uppercase, length ≥ 6, no non-alphanumeric required).
- **NuGet versions:** use the versions named in Task 1. If restore fails because a version does not exist, take the latest stable of that package and note the substitution in the commit message.

---

## File Structure

**Backend integration harness** — `BE/Tests.Integration/`

| File | Responsibility |
|---|---|
| `Tests.Integration.csproj` | Project + package references, added to `BE.slnx` |
| `Harness/TestDatabase.cs` | Create / drop `chat_test_*`, reap stale ones |
| `Harness/RemoteIpStartupFilter.cs` | Give `TestServer` requests a remote IP so `SessionsController` records one |
| `Harness/ChatAppFactory.cs` | `WebApplicationFactory<Program>` pointed at a test database |
| `Harness/ChatAppFixture.cs` | Collection fixture: owns the database, migrates it, exposes a Respawn reset |
| `Harness/AuthHelper.cs` | Register + sign in + register a session; hand back a ready `HttpClient` |
| `Harness/HubClient.cs` | SignalR connection over `TestServer`, with `WaitFor<T>` |
| `Harness/IntegrationTestBase.cs` | Per-test reset, shared accessors |
| `Sessions/SessionsTests.cs` | The `user-sessions` server-side scenarios |
| `coverlet.runsettings` | Coverage exclusions |

**Frontend unit harness** — `FE/src/test/`

| File | Responsibility |
|---|---|
| `setup.ts` | jest-dom, MSW lifecycle, per-test store/storage reset |
| `server.ts` | MSW `setupServer` + default handlers |
| `renderWithProviders.tsx` | QueryClientProvider + MemoryRouter wrapper, seedable auth |
| `fakeHub.ts` | Stand-in for `realtime/hubClient.ts`; records invocations, emits server events |
| `specTest.ts` | `specTest(id, name, fn)` — prefixes the `@spec:` marker onto the title |

**End-to-end harness** — `FE/e2e/`

| File | Responsibility |
|---|---|
| `playwright.config.ts` (in `FE/`) | baseURL, JSON reporter, trace on failure |
| `globalSetup.ts` | Assert the stack is up, fail with an actionable message |
| `fixtures.ts` | `signedInUser`, `twoContextsSameUser`, `specTest` |
| `sessions.spec.ts` | The `user-sessions` cross-cutting scenarios |

**Traceability tooling** — `tools/spec-coverage/`

| File | Responsibility |
|---|---|
| `package.json` | `{"type": "module"}`, zero dependencies |
| `parseSpecs.js` | `openspec/specs/**/spec.md` → scenario identifiers |
| `readResults.js` | TRX / vitest JSON / Playwright JSON → normalized claims |
| `report.js` | Cross-reference, render markdown + console summary, decide exit code |
| `index.js` | Entry point wiring the three together |

**Orchestration** — `test.bat` at the repo root.

---

### Task 1: Backend test project with an isolated, migrated database

Establishes the substrate from design D1 and D2. Deliverable: a test that proves each collection gets its own migrated database, that Respawn resets it between tests, and that stale databases are reaped.

**Files:**
- Create: `docker-compose.tests.yml`
- Modify: `BE/Web/Program.cs` (append after the closing `}` of the `finally` block)
- Modify: `BE/BE.slnx`
- Modify: `.gitignore`
- Create: `BE/Tests.Integration/Tests.Integration.csproj`
- Create: `BE/Tests.Integration/Harness/TestDatabase.cs`
- Create: `BE/Tests.Integration/Harness/RemoteIpStartupFilter.cs`
- Create: `BE/Tests.Integration/Harness/ChatAppFactory.cs`
- Create: `BE/Tests.Integration/Harness/ChatAppFixture.cs`
- Create: `BE/Tests.Integration/Harness/IntegrationTestBase.cs`
- Create: `BE/Tests.Integration/coverlet.runsettings`
- Test: `BE/Tests.Integration/Harness/HarnessTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ChatAppFixture` — `HttpClient CreateClient()`, `ChatAppFactory Factory { get; }`, `Task ResetAsync()`, `IServiceScope CreateScope()`
  - `IntegrationTestBase` — abstract base with `protected ChatAppFixture Fixture { get; }`, implements `IAsyncLifetime` calling `Fixture.ResetAsync()` in `InitializeAsync`
  - `TestDatabase` — `static Task<TestDatabase> CreateAsync()`, `string ConnectionString { get; }`, `ValueTask DisposeAsync()`
  - Collection name constant: `public const string CollectionName = "chat-app"` on `ChatAppCollection`

- [ ] **Step 1: Publish the database port in a tests-only overlay**

Do **not** touch `docker-compose.yml`. Create `docker-compose.tests.yml` at the repo root:

```yaml
# Test-only overlay. Publishes the database so host-run integration tests can
# reach it. Deliberately NOT in docker-compose.yml: platform-constraints
# requires the database to be unreachable from outside the internal network
# when the stack is brought up normally.
#
#   docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db
services:
  db:
    ports:
      - "55432:5432"
```

Port 55432 rather than 5432 to avoid colliding with an installed PostgreSQL.

- [ ] **Step 2: Verify the port is published only with the overlay**

First confirm the base stack stays closed:

```bash
docker compose up -d db
docker compose exec db pg_isready -U postgres
powershell -Command "(Test-NetConnection -ComputerName localhost -Port 55432 -WarningAction SilentlyContinue).TcpTestSucceeded"
```

Expected: `pg_isready` reports accepting connections, and the port test reports `False` — the base compose file publishes nothing.

Then bring it up with the overlay:

```bash
docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db
powershell -Command "(Test-NetConnection -ComputerName localhost -Port 55432 -WarningAction SilentlyContinue).TcpTestSucceeded"
```

Expected: `True`.

- [ ] **Step 3: Make the entry point addressable**

Append to the very end of `BE/Web/Program.cs`, after the closing brace of the `finally` block:

```csharp

// Required by WebApplicationFactory<Program> in BE/Tests.Integration.
// Top-level statements generate an internal Program class; this marker makes it public.
// Do not remove.
public partial class Program { }
```

- [ ] **Step 4: Create the test project and register it**

```bash
cd BE
dotnet new xunit3 -n Tests.Integration -o Tests.Integration
dotnet sln BE.slnx add Tests.Integration/Tests.Integration.csproj
```

If the `xunit3` template is unavailable, run `dotnet new install xunit.v3.templates` first.

Replace `BE/Tests.Integration/Tests.Integration.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageReference Include="xunit.v3" Version="3.0.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.0" />
    <PackageReference Include="Respawn" Version="6.2.1" />
    <PackageReference Include="Npgsql" Version="9.0.3" />
    <PackageReference Include="FluentAssertions" Version="7.0.0" />
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Web/Web.csproj" />
  </ItemGroup>

</Project>
```

Delete the template's generated `UnitTest1.cs`.

- [ ] **Step 5: Verify the project restores and builds**

Run: `cd BE && dotnet build Tests.Integration/Tests.Integration.csproj`
Expected: build succeeds. If a package version does not exist, take the latest stable and note it in the commit message (Global Constraints).

- [ ] **Step 6: Write the failing harness test**

Create `BE/Tests.Integration/Harness/HarnessTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Integration.Harness;

[Collection(ChatAppCollection.CollectionName)]
public class HarnessTests : IntegrationTestBase
{
    public HarnessTests(ChatAppFixture fixture) : base(fixture) { }

    [Fact(DisplayName = "Test database is isolated and migrated")]
    public async Task Database_is_isolated_and_migrated()
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Database.GetDbConnection().Database.Should().StartWith("chat_test_");
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.UserSessions.CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "Respawn clears data between tests")]
    public async Task Respawn_clears_data_between_tests()
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The previous test may have created rows; InitializeAsync reset them.
        (await db.Users.CountAsync()).Should().Be(0);
    }

    [Fact(DisplayName = "Host serves requests and rejects anonymous callers")]
    public async Task Host_serves_requests()
    {
        var client = Fixture.CreateClient();

        var response = await client.GetAsync("/api/Sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 7: Run it to verify it fails**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj`
Expected: compile errors — `ChatAppCollection`, `IntegrationTestBase`, `ChatAppFixture` do not exist.

- [ ] **Step 8: Implement `TestDatabase`**

Create `BE/Tests.Integration/Harness/TestDatabase.cs`. The 24-hour reaping rule and the `chat_test_` prefix restriction come from design D1 Risks — nothing here may touch the `chat` database.

```csharp
using Npgsql;

namespace Tests.Integration.Harness;

/// <summary>
/// Owns one throwaway PostgreSQL database on the compose server.
/// Never connects to the development `chat` database except through the
/// admin connection below, which only issues CREATE/DROP for chat_test_* names.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private const string AdminConnectionString =
        "Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=postgres;Include Error Detail=true";

    private const string Prefix = "chat_test_";

    public string Name { get; }
    public string ConnectionString { get; }

    private TestDatabase(string name)
    {
        Name = name;
        ConnectionString =
            $"Host=localhost;Port=55432;Database={name};Username=postgres;Password=postgres;Include Error Detail=true";
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        await EnsureServerReachableAsync();
        await ReapStaleDatabasesAsync();

        var name = Prefix + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        await cmd.ExecuteNonQueryAsync();

        return new TestDatabase(name);
    }

    /// <summary>Fail fast with an actionable message rather than test-by-test connection errors.</summary>
    private static async Task EnsureServerReachableAsync()
    {
        try
        {
            await using var admin = new NpgsqlConnection(AdminConnectionString);
            await admin.OpenAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Cannot reach PostgreSQL at localhost:55432. " +
                "Backend integration tests need the compose database running WITH the test overlay. " +
                "Start it with:  docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db",
                ex);
        }
    }

    /// <summary>
    /// Drops chat_test_* databases older than 24 hours, so an aborted run
    /// never requires manual cleanup and never races a live run.
    /// </summary>
    private static async Task ReapStaleDatabasesAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();

        var stale = new List<string>();
        await using (var query = new NpgsqlCommand(
            """
            SELECT d.datname
            FROM pg_database d
            LEFT JOIN pg_stat_file('base/' || d.oid || '/PG_VERSION') AS f ON true
            WHERE d.datname LIKE 'chat_test_%'
              AND f.modification < now() - interval '24 hours'
            """, admin))
        await using (var reader = await query.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) stale.Add(reader.GetString(0));
        }

        foreach (var name in stale)
        {
            try
            {
                await using var drop = new NpgsqlCommand(
                    $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
                await drop.ExecuteNonQueryAsync();
            }
            catch
            {
                // A concurrent run may hold it; leave it for the next reap.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)", admin);
        await cmd.ExecuteNonQueryAsync();
    }
}
```

- [ ] **Step 9: Implement the remote-IP startup filter**

`TestServer` leaves `HttpContext.Connection.RemoteIpAddress` null, but `SessionsController.Register` records it. Without this, the "device details captured" scenario in Task 4 cannot pass.

Create `BE/Tests.Integration/Harness/RemoteIpStartupFilter.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Tests.Integration.Harness;

/// <summary>
/// TestServer does not populate Connection.RemoteIpAddress. SessionsController
/// records it, so supply a loopback address at the very front of the pipeline.
/// </summary>
public sealed class RemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                await nextMiddleware();
            });
            next(app);
        };
}
```

- [ ] **Step 10: Implement `ChatAppFactory`**

Create `BE/Tests.Integration/Harness/ChatAppFactory.cs`. The uploads-root override matters because `LocalFileStorage` is constructed eagerly as a singleton against `/var/app/uploads`, which is not a writable path on a Windows host.

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Integration.Harness;

public sealed class ChatAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _uploadsRoot;

    public ChatAppFactory(string connectionString)
    {
        _connectionString = connectionString;
        _uploadsRoot = Path.Combine(Path.GetTempPath(), "chat-test-uploads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_uploadsRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("Uploads:Root", _uploadsRoot);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, RemoteIpStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_uploadsRoot))
        {
            try { Directory.Delete(_uploadsRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
```

- [ ] **Step 11: Implement the collection fixture**

Create `BE/Tests.Integration/Harness/ChatAppFixture.cs`:

```csharp
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Xunit;

namespace Tests.Integration.Harness;

public sealed class ChatAppFixture : IAsyncLifetime
{
    private TestDatabase _database = null!;
    private Respawner _respawner = null!;
    private NpgsqlConnection _respawnConnection = null!;

    public ChatAppFactory Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        Factory = new ChatAppFactory(_database.ConnectionString);

        // Migrate explicitly. Program.cs also migrates at startup but swallows
        // failures, so run it here where an error is loud.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
        }

        _respawnConnection = new NpgsqlConnection(_database.ConnectionString);
        await _respawnConnection.OpenAsync();
        _respawner = await Respawner.CreateAsync(_respawnConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
    }

    public HttpClient CreateClient() => Factory.CreateClient();

    public IServiceScope CreateScope() => Factory.Services.CreateScope();

    public Task ResetAsync() => _respawner.ResetAsync(_respawnConnection);

    public async ValueTask DisposeAsync()
    {
        await _respawnConnection.DisposeAsync();
        Factory.Dispose();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(CollectionName)]
public class ChatAppCollection : ICollectionFixture<ChatAppFixture>
{
    public const string CollectionName = "chat-app";
}
```

- [ ] **Step 12: Implement the test base**

Create `BE/Tests.Integration/Harness/IntegrationTestBase.cs`:

```csharp
using Xunit;

namespace Tests.Integration.Harness;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected ChatAppFixture Fixture { get; }

    protected IntegrationTestBase(ChatAppFixture fixture) => Fixture = fixture;

    /// <summary>Every test starts from an empty database, so order never matters.</summary>
    public virtual ValueTask InitializeAsync() => new(Fixture.ResetAsync());

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 13: Add coverage exclusions**

Create `BE/Tests.Integration/coverlet.runsettings` (design D7):

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat code coverage">
        <Configuration>
          <Format>cobertura</Format>
          <Exclude>[Tests.Integration]*,[Tests.Load]*</Exclude>
          <ExcludeByFile>**/Migrations/**/*.cs,**/Program.cs</ExcludeByFile>
          <ExcludeByAttribute>Obsolete,GeneratedCodeAttribute,CompilerGeneratedAttribute</ExcludeByAttribute>
          <SkipAutoProps>true</SkipAutoProps>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

- [ ] **Step 14: Ignore generated artifacts**

Append to `.gitignore`:

```
/docs/test-coverage/backend/
/docs/test-coverage/frontend/
/BE/Tests.Integration/TestResults/
/FE/test-results/
/FE/playwright-report/
/FE/coverage/
/FE/.vitest-report.json
```

- [ ] **Step 15: Run the harness test to verify it passes**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj`
Expected: 3 passed.

- [ ] **Step 16: Verify isolation holds across runs**

Run the same command a second time with no cleanup. Expected: 3 passed, identical result (spec: "Independent runs").

Then confirm nothing leaked:

```bash
docker compose exec db psql -U postgres -c "SELECT datname FROM pg_database WHERE datname LIKE 'chat_test_%'"
```

Expected: zero rows.

- [ ] **Step 17: Verify the fail-fast path**

```bash
docker compose stop db
cd BE && dotnet test Tests.Integration/Tests.Integration.csproj
```

Expected: failure whose message contains "Cannot reach PostgreSQL at localhost:55432" and the overlay command (spec: "Database unavailable").

Restart it: `docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db`

- [ ] **Step 18: Commit**

```bash
git add docker-compose.tests.yml BE/Web/Program.cs BE/BE.slnx BE/Tests.Integration .gitignore
git commit -m "test: add backend integration harness with isolated per-collection database"
```

---

### Task 2: Authenticated client helper

Deliverable: a helper that turns a fresh user into a ready-to-use `HttpClient`, proven by a test that reaches an authorized endpoint.

**Files:**
- Create: `BE/Tests.Integration/Harness/AuthHelper.cs`
- Test: `BE/Tests.Integration/Harness/AuthHelperTests.cs`

**Interfaces:**
- Consumes: `ChatAppFixture.CreateClient()`, `IntegrationTestBase`, `ChatAppCollection.CollectionName` (Task 1).
- Produces:
  - `record TestUser(string Email, string Username, string AccessToken, HttpClient Client)` with `Guid? SessionId { get; set; }`
  - `AuthHelper.RegisterAndSignInAsync(ChatAppFixture fixture, string? userAgent = null)` → `Task<TestUser>`
  - `AuthHelper.RegisterSessionAsync(TestUser user, string? deviceInfo = null)` → `Task<Guid>` (sets `user.SessionId` and the `X-Session-Id` default header)

- [ ] **Step 1: Write the failing test**

Create `BE/Tests.Integration/Harness/AuthHelperTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Tests.Integration.Harness;

[Collection(ChatAppCollection.CollectionName)]
public class AuthHelperTests : IntegrationTestBase
{
    public AuthHelperTests(ChatAppFixture fixture) : base(fixture) { }

    [Fact(DisplayName = "Helper produces a client that reaches authorized endpoints")]
    public async Task Helper_produces_authorized_client()
    {
        var user = await AuthHelper.RegisterAndSignInAsync(Fixture);

        var response = await user.Client.GetAsync("/api/Sessions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<object>>())!.Should().BeEmpty();
    }

    [Fact(DisplayName = "Helper registers a session and sends its id")]
    public async Task Helper_registers_a_session()
    {
        var user = await AuthHelper.RegisterAndSignInAsync(Fixture);

        var sessionId = await AuthHelper.RegisterSessionAsync(user, "Test Device");

        sessionId.Should().NotBeEmpty();
        user.SessionId.Should().Be(sessionId);
        user.Client.DefaultRequestHeaders.GetValues("X-Session-Id").Should().ContainSingle()
            .Which.Should().Be(sessionId.ToString());
    }

    [Fact(DisplayName = "Two helper users are independent")]
    public async Task Two_users_are_independent()
    {
        var alice = await AuthHelper.RegisterAndSignInAsync(Fixture);
        var bob = await AuthHelper.RegisterAndSignInAsync(Fixture);

        alice.Email.Should().NotBe(bob.Email);
        alice.AccessToken.Should().NotBe(bob.AccessToken);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --filter "FullyQualifiedName~AuthHelperTests"`
Expected: compile error — `AuthHelper` does not exist.

- [ ] **Step 3: Implement `AuthHelper`**

Registration goes through `POST /api/auth/register` (`{ email, username, password }`) and sign-in through `POST /api/auth/login` (`{ email, password }`), which returns the Identity bearer token payload — see `BE/Web/Controllers/AuthController.cs` and `FE/src/api/auth.ts`.

Create `BE/Tests.Integration/Harness/AuthHelper.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Tests.Integration.Harness;

public sealed record TestUser(string Email, string Username, string AccessToken, HttpClient Client)
{
    public Guid? SessionId { get; set; }
}

public static class AuthHelper
{
    public const string Password = "Passw0rd";

    private sealed record AccessTokenResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("refreshToken")] string RefreshToken);

    private sealed record SessionRegistration(
        [property: JsonPropertyName("id")] Guid Id);

    public static async Task<TestUser> RegisterAndSignInAsync(
        ChatAppFixture fixture,
        string? userAgent = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var email = $"user-{suffix}@example.test";
        var username = $"user{suffix}";

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent ?? "IntegrationTests/1.0");

        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { email, username, password = Password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var tokens = await login.Content.ReadFromJsonAsync<AccessTokenResponse>()
            ?? throw new InvalidOperationException("Login returned no token payload.");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return new TestUser(email, username, tokens.AccessToken, client);
    }

    public static async Task<Guid> RegisterSessionAsync(TestUser user, string? deviceInfo = null)
    {
        var response = await user.Client.PostAsJsonAsync("/api/Sessions/register",
            new { deviceInfo });
        response.EnsureSuccessStatusCode();

        var registration = await response.Content.ReadFromJsonAsync<SessionRegistration>()
            ?? throw new InvalidOperationException("Session registration returned no id.");

        user.SessionId = registration.Id;
        user.Client.DefaultRequestHeaders.Remove("X-Session-Id");
        user.Client.DefaultRequestHeaders.Add("X-Session-Id", registration.Id.ToString());

        return registration.Id;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --filter "FullyQualifiedName~AuthHelperTests"`
Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add BE/Tests.Integration/Harness/AuthHelper.cs BE/Tests.Integration/Harness/AuthHelperTests.cs
git commit -m "test: add authenticated client helper to integration harness"
```

---

### Task 3: Real-time client helper

Deliverable: two authenticated SignalR clients connected over `TestServer`, where one observes an event caused by the other, with no ports and no sleeps (design D3, spec: "Real-time behavior at the integration layer").

**Files:**
- Create: `BE/Tests.Integration/Harness/HubClient.cs`
- Test: `BE/Tests.Integration/Harness/HubClientTests.cs`

**Interfaces:**
- Consumes: `ChatAppFixture.Factory`, `TestUser` (Tasks 1–2).
- Produces:
  - `HubClient.ConnectAsync(ChatAppFixture fixture, TestUser user)` → `Task<HubClient>`
  - `HubClient.On<T>(string eventName, TimeSpan? timeout = null)` → `Task<T>` — arms a one-shot awaiter that must be created **before** the triggering action; throws `TimeoutException` naming the event if it never arrives
  - `HubClient.InvokeAsync(string method, params object?[] args)` → `Task`
  - `HubClient.InvokeAsync<TResult>(string method, params object?[] args)` → `Task<TResult>`
  - `HubClient.DisposeAsync()`
  - `HubClient.DefaultTimeout` — `TimeSpan.FromSeconds(10)`

- [ ] **Step 1: Write the failing test**

Create `BE/Tests.Integration/Harness/HubClientTests.cs`. `GetPresenceFor` is a hub method that returns a value, and `Ping` fans `PresenceChanged` out to other connections — both are on `ChatHub`, so this exercises invocation and server-push in one test.

```csharp
using FluentAssertions;
using Xunit;

namespace Tests.Integration.Harness;

[Collection(ChatAppCollection.CollectionName)]
public class HubClientTests : IntegrationTestBase
{
    public HubClientTests(ChatAppFixture fixture) : base(fixture) { }

    [Fact(DisplayName = "Hub client connects and invokes a method with a result")]
    public async Task Hub_client_invokes()
    {
        var user = await AuthHelper.RegisterAndSignInAsync(Fixture);
        await using var hub = await HubClient.ConnectAsync(Fixture, user);

        var presence = await hub.InvokeAsync<Dictionary<string, string>>(
            "GetPresenceFor", new[] { Guid.NewGuid().ToString() });

        presence.Should().NotBeNull();
    }

    [Fact(DisplayName = "Awaiting an event that never arrives times out rather than hanging")]
    public async Task Missing_event_times_out()
    {
        var user = await AuthHelper.RegisterAndSignInAsync(Fixture);
        await using var hub = await HubClient.ConnectAsync(Fixture, user);

        var waiting = hub.On<object>("MessageReceived", TimeSpan.FromMilliseconds(250));

        await FluentActions.Awaiting(() => waiting)
            .Should().ThrowAsync<TimeoutException>()
            .WithMessage("*MessageReceived*");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --filter "FullyQualifiedName~HubClientTests"`
Expected: compile error — `HubClient` does not exist.

- [ ] **Step 3: Implement `HubClient`**

Create `BE/Tests.Integration/Harness/HubClient.cs`:

```csharp
using Microsoft.AspNetCore.SignalR.Client;

namespace Tests.Integration.Harness;

/// <summary>
/// A SignalR client that talks to the in-process TestServer. No sockets, no ports.
/// Event assertions are TaskCompletionSource-based with a bounded timeout so a
/// missing event fails fast instead of hanging, and a delivered event resolves
/// immediately instead of costing a fixed sleep.
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HubConnection _connection;
    private readonly List<IDisposable> _handlers = [];

    private HubClient(HubConnection connection) => _connection = connection;

    public static async Task<HubClient> ConnectAsync(ChatAppFixture fixture, TestUser user)
    {
        var server = fixture.Factory.Server;

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(user.AccessToken);
            })
            .Build();

        await connection.StartAsync();
        return new HubClient(connection);
    }

    /// <summary>
    /// Arms a one-shot awaiter for the next occurrence of <paramref name="eventName"/>.
    /// Call this BEFORE the action that triggers the event, then await the returned task.
    /// </summary>
    public Task<T> On<T>(string eventName, TimeSpan? timeout = null)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = _connection.On<T>(eventName, payload => completion.TrySetResult(payload));
        _handlers.Add(handler);

        return AwaitWithTimeout(completion.Task, timeout ?? DefaultTimeout, eventName);
    }

    private static async Task<T> AwaitWithTimeout<T>(Task<T> task, TimeSpan timeout, string eventName)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completed != task)
        {
            throw new TimeoutException(
                $"Timed out after {timeout.TotalSeconds:0.##}s waiting for hub event '{eventName}'.");
        }
        cts.Cancel();
        return await task;
    }

    public Task InvokeAsync(string method, params object?[] args) =>
        _connection.InvokeCoreAsync(method, args);

    public Task<TResult> InvokeAsync<TResult>(string method, params object?[] args) =>
        _connection.InvokeCoreAsync<TResult>(method, args);

    public async ValueTask DisposeAsync()
    {
        foreach (var handler in _handlers) handler.Dispose();
        await _connection.DisposeAsync();
    }
}
```

Note: the single `Task.Delay(Timeout.Infinite, cts.Token)` above is the timeout mechanism itself, not a wait-for-things-to-settle sleep. It is cancelled the instant the event arrives. This is the only `Task.Delay` permitted anywhere in the suite.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --filter "FullyQualifiedName~HubClientTests"`
Expected: 2 passed.

- [ ] **Step 5: Run the whole backend suite**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj`
Expected: 8 passed.

- [ ] **Step 6: Commit**

```bash
git add BE/Tests.Integration/Harness/HubClient.cs BE/Tests.Integration/Harness/HubClientTests.cs
git commit -m "test: add in-process SignalR client helper to integration harness"
```

---

### Task 4: `user-sessions` backend integration tests

Deliverable: the server-side `user-sessions` scenarios verified through the real HTTP pipeline, each declaring its scenario identifier.

**Files:**
- Create: `BE/Tests.Integration/Sessions/SessionsTests.cs`
- Delete: `BE/Tests.Integration/Harness/HarnessTests.cs` (replaced by real coverage; the isolation guarantees it proved are now exercised by every test in this file)

**Interfaces:**
- Consumes: `AuthHelper.RegisterAndSignInAsync`, `AuthHelper.RegisterSessionAsync`, `IntegrationTestBase` (Tasks 1–2).
- Produces: nothing consumed by later tasks.

**Scenario identifiers claimed here** (derived from `openspec/specs/user-sessions/spec.md`):
- `user-sessions/session-registration-per-browser/new-sign-in-registers-a-session`
- `user-sessions/listing-active-sessions/viewing-sessions`
- `user-sessions/listing-active-sessions/only-own-sessions`
- `user-sessions/revoking-sessions/revoking-another-session`
- `user-sessions/revoking-sessions/revoking-a-foreign-session`

- [ ] **Step 1: Write the failing tests**

Create `BE/Tests.Integration/Sessions/SessionsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Tests.Integration.Harness;
using Xunit;

namespace Tests.Integration.Sessions;

[Collection(ChatAppCollection.CollectionName)]
public class SessionsTests : IntegrationTestBase
{
    public SessionsTests(ChatAppFixture fixture) : base(fixture) { }

    private sealed record SessionView(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("deviceInfo")] string? DeviceInfo,
        [property: JsonPropertyName("userAgent")] string? UserAgent,
        [property: JsonPropertyName("ipAddress")] string? IpAddress,
        [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
        [property: JsonPropertyName("lastSeenAt")] DateTime LastSeenAt,
        [property: JsonPropertyName("isCurrent")] bool IsCurrent);

    private static async Task<List<SessionView>> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/Sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<SessionView>>())!;
    }

    [Fact(DisplayName = "@spec:user-sessions/session-registration-per-browser/new-sign-in-registers-a-session Registering a session records device details and timestamps")]
    public async Task Registration_records_device_details()
    {
        var user = await AuthHelper.RegisterAndSignInAsync(Fixture, userAgent: "TestAgent/9.9");

        var sessionId = await AuthHelper.RegisterSessionAsync(user, "Windows Test Box");

        var sessions = await ListAsync(user.Client);
        var session = sessions.Should().ContainSingle().Subject;

        session.Id.Should().Be(sessionId);
        session.DeviceInfo.Should().Be("Windows Test Box");
        session.UserAgent.Should().Contain("TestAgent/9.9");
        session.IpAddress.Should().NotBeNullOrWhiteSpace();
        session.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        session.LastSeenAt.Should().BeCloseTo(session.CreatedAt, TimeSpan.FromSeconds(5));
    }

    [Fact(DisplayName = "@spec:user-sessions/listing-active-sessions/viewing-sessions Lists active sessions most recently seen first with the current one marked")]
    public async Task Lists_sessions_newest_first_with_current_marked()
    {
        var user = await AuthHelper.RegisterAndSignInAsync(Fixture);

        var older = await AuthHelper.RegisterSessionAsync(user, "Older Device");
        var newer = await AuthHelper.RegisterSessionAsync(user, "Newer Device");
        // RegisterSessionAsync leaves X-Session-Id pointing at `newer`.

        var sessions = await ListAsync(user.Client);

        sessions.Should().HaveCount(2);
        sessions[0].Id.Should().Be(newer, "sessions are ordered by last-seen descending");
        sessions[1].Id.Should().Be(older);
        sessions.Single(s => s.Id == newer).IsCurrent.Should().BeTrue();
        sessions.Single(s => s.Id == older).IsCurrent.Should().BeFalse();
    }

    [Fact(DisplayName = "@spec:user-sessions/listing-active-sessions/only-own-sessions Listing never returns another account's sessions")]
    public async Task Listing_returns_only_own_sessions()
    {
        var alice = await AuthHelper.RegisterAndSignInAsync(Fixture);
        var bob = await AuthHelper.RegisterAndSignInAsync(Fixture);

        var aliceSession = await AuthHelper.RegisterSessionAsync(alice, "Alice Device");
        var bobSession = await AuthHelper.RegisterSessionAsync(bob, "Bob Device");

        var aliceSessions = await ListAsync(alice.Client);

        aliceSessions.Should().ContainSingle().Which.Id.Should().Be(aliceSession);
        aliceSessions.Should().NotContain(s => s.Id == bobSession);
    }

    [Fact(DisplayName = "@spec:user-sessions/revoking-sessions/revoking-another-session Revoking another session removes it and leaves the caller signed in")]
    public async Task Revoking_another_session_leaves_caller_signed_in()
    {
        var user = await AuthHelper.RegisterAndSignInAsync(Fixture);
        var other = await AuthHelper.RegisterSessionAsync(user, "Other Device");
        var current = await AuthHelper.RegisterSessionAsync(user, "This Device");

        var response = await user.Client.DeleteAsync($"/api/Sessions/{other}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var remaining = await ListAsync(user.Client);
        remaining.Should().ContainSingle().Which.Id.Should().Be(current);
    }

    [Fact(DisplayName = "@spec:user-sessions/revoking-sessions/revoking-a-foreign-session Revoking a session belonging to another account is refused and revokes nothing")]
    public async Task Revoking_a_foreign_session_is_refused()
    {
        var alice = await AuthHelper.RegisterAndSignInAsync(Fixture);
        var bob = await AuthHelper.RegisterAndSignInAsync(Fixture);
        await AuthHelper.RegisterSessionAsync(alice, "Alice Device");
        var bobSession = await AuthHelper.RegisterSessionAsync(bob, "Bob Device");

        var response = await alice.Client.DeleteAsync($"/api/Sessions/{bobSession}");

        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().Contain("Session.NotFound");

        var bobSessions = await ListAsync(bob.Client);
        bobSessions.Should().ContainSingle().Which.Id.Should().Be(bobSession,
            "a refused revoke must not remove the session");
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --filter "FullyQualifiedName~SessionsTests"`
Expected: FAIL — the file compiles but the tests have never run; confirm each failure is an assertion failure or a genuine gap, not a harness error.

If `Lists_sessions_newest_first_with_current_marked` fails on ordering because both sessions share a `LastSeenAt` to the tick, that is a real ordering ambiguity — record it under `## Findings` per Global Constraints and relax the assertion to "contains both, current marked correctly".

- [ ] **Step 3: Remove the scaffolding tests**

```bash
git rm BE/Tests.Integration/Harness/HarnessTests.cs
```

Their guarantees (isolation, migration, anonymous rejection) are now exercised by every test in `SessionsTests`, which runs against a reset database and an authorized client.

- [ ] **Step 4: Run the full backend suite to verify it passes**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj`
Expected: 10 passed (5 sessions + 3 auth helper + 2 hub client).

- [ ] **Step 5: Verify order independence**

Run a single test alone:

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --filter "FullyQualifiedName~Revoking_a_foreign_session_is_refused"`
Expected: 1 passed (spec: "Order independence").

- [ ] **Step 6: Commit**

```bash
git add BE/Tests.Integration/Sessions/SessionsTests.cs
git commit -m "test: verify user-sessions server rules through the HTTP pipeline"
```

---

### Task 5: Frontend test environment

Deliverable: a vitest setup where a component can be rendered with real providers, its HTTP traffic intercepted, and its scenario claim declared — proven by one test.

**Files:**
- Modify: `FE/package.json`
- Modify: `FE/vite.config.ts`
- Create: `FE/src/test/setup.ts`
- Create: `FE/src/test/server.ts`
- Create: `FE/src/test/renderWithProviders.tsx`
- Create: `FE/src/test/specTest.ts`
- Test: `FE/src/test/harness.test.tsx`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `server` — MSW `SetupServerApi` from `FE/src/test/server.ts`, plus `defaultHandlers`
  - `renderWithProviders(ui, options?)` → `{ ...RenderResult, queryClient }`; `options` is `{ route?: string; accessToken?: string | null }`
  - `createTestQueryClient()` → `QueryClient` with retries disabled
  - `specTest(scenarioId, name, fn)` and `specTest.skip(...)` — vitest wrappers that prefix `@spec:<id>` onto the title

- [ ] **Step 1: Add the missing dev dependencies**

```bash
cd FE
npm install -D jsdom@25 @vitest/coverage-v8@4
```

- [ ] **Step 2: Configure vitest**

Replace the import line and append a `test` block in `FE/vite.config.ts`. Change the first import from `vite` to `vitest/config` so the `test` key type-checks:

```ts
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import path from 'path'
```

Then add this `test` property to the config object, as a sibling of `plugins`, `resolve`, and `server`:

```ts
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
    exclude: ['e2e/**', 'node_modules/**'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html', 'json-summary'],
      reportsDirectory: '../docs/test-coverage/frontend',
      include: ['src/**/*.{ts,tsx}'],
      exclude: [
        'src/main.tsx',
        'src/routes.tsx',
        'src/types/**',
        'src/test/**',
        'src/**/*.test.{ts,tsx}',
      ],
      // The 80% line threshold is evaluated by test.bat from coverage-summary.json,
      // NOT enforced here. A vitest-level threshold would fail the run outright
      // while only user-sessions has tests, which would mask real test failures
      // behind a coverage failure. Flip to enforcing by adding
      //   thresholds: { lines: 80 }
      // once every capability has tests. See design.md D7 and the Non-Goals.
    },
  },
```

- [ ] **Step 3: Add the test scripts**

In `FE/package.json`, replace the `scripts` block with:

```json
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "preview": "vite preview",
    "test": "vitest run",
    "test:watch": "vitest",
    "test:coverage": "vitest run --coverage --reporter=json --outputFile=.vitest-report.json",
    "e2e": "playwright test"
  },
```

- [ ] **Step 4: Write the failing harness test**

Create `FE/src/test/harness.test.tsx`:

```tsx
import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { expect } from 'vitest'
import { useQuery } from '@tanstack/react-query'
import { listSessions } from '@/api/sessions'
import { renderWithProviders } from './renderWithProviders'
import { server } from './server'
import { specTest } from './specTest'

function Probe() {
  const { data } = useQuery({ queryKey: ['sessions'], queryFn: listSessions })
  return <div>{data ? `count:${data.length}` : 'loading'}</div>
}

specTest(
  'user-sessions/listing-active-sessions/viewing-sessions',
  'harness renders a component whose HTTP call is intercepted',
  async () => {
    let sentSessionId: string | null = null
    server.use(
      http.get('/api/Sessions', ({ request }) => {
        sentSessionId = request.headers.get('X-Session-Id')
        return HttpResponse.json([
          {
            id: 'aaaaaaaa-0000-0000-0000-000000000001',
            deviceInfo: 'Probe Device',
            userAgent: 'probe',
            ipAddress: '127.0.0.1',
            createdAt: '2026-01-01T00:00:00Z',
            lastSeenAt: '2026-01-01T00:00:00Z',
            isCurrent: true,
          },
        ])
      })
    )
    localStorage.setItem('sessionId', 'session-abc')

    renderWithProviders(<Probe />, { accessToken: 'token-123' })

    await waitFor(() => expect(screen.getByText('count:1')).toBeInTheDocument())
    expect(sentSessionId).toBe('session-abc')
  }
)
```

- [ ] **Step 5: Run it to verify it fails**

Run: `cd FE && npm test`
Expected: FAIL — cannot resolve `./renderWithProviders`, `./server`, `./specTest`.

- [ ] **Step 6: Implement the MSW server**

Create `FE/src/test/server.ts`. Handlers here are the safety net; individual tests override with `server.use(...)`.

```ts
import { setupServer } from 'msw/node'
import { http, HttpResponse } from 'msw'

export const defaultHandlers = [
  http.post('/api/auth/register', () => new HttpResponse(null, { status: 200 })),
  http.post('/api/auth/login', () =>
    HttpResponse.json({ accessToken: 'test-access', refreshToken: 'test-refresh', expiresIn: 3600 })
  ),
  http.post('/refresh', () =>
    HttpResponse.json({ accessToken: 'refreshed-access', refreshToken: 'refreshed-refresh', expiresIn: 3600 })
  ),
  http.get('/api/Me', () =>
    HttpResponse.json({ id: 'user-1', userName: 'tester', email: 'tester@example.test' })
  ),
  http.post('/api/Sessions/register', () =>
    HttpResponse.json({ id: '11111111-1111-1111-1111-111111111111' })
  ),
  http.get('/api/Sessions', () => HttpResponse.json([])),
  http.delete('/api/Sessions/current', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/Sessions/:id', () => new HttpResponse(null, { status: 204 })),
]

export const server = setupServer(...defaultHandlers)
```

If the `/api/Me` path does not match what `FE/src/api/me.ts` requests, correct the handler path to match the source — the source is authoritative.

- [ ] **Step 7: Implement the setup file**

Create `FE/src/test/setup.ts`:

```ts
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { server } from './server'
import { useAuthStore } from '@/stores/authStore'

// MSW must intercept relative URLs; jsdom needs an origin for them to resolve.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

afterEach(() => {
  cleanup()
  server.resetHandlers()
  localStorage.clear()
  sessionStorage.clear()
  useAuthStore.setState({
    accessToken: null,
    refreshToken: null,
    me: null,
    keepSignedIn: false,
  })
})

afterAll(() => server.close())
```

- [ ] **Step 8: Implement the render helper**

Create `FE/src/test/renderWithProviders.tsx`:

```tsx
import type { ReactElement, ReactNode } from 'react'
import { render, type RenderResult } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { useAuthStore } from '@/stores/authStore'

export function createTestQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0, staleTime: 0 },
      mutations: { retry: false },
    },
  })
}

export interface RenderOptions {
  route?: string
  accessToken?: string | null
}

export function renderWithProviders(
  ui: ReactElement,
  { route = '/', accessToken = null }: RenderOptions = {}
): RenderResult & { queryClient: QueryClient } {
  if (accessToken) {
    useAuthStore.setState({ accessToken, refreshToken: 'test-refresh' })
  }

  const queryClient = createTestQueryClient()

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>{children}</MemoryRouter>
    </QueryClientProvider>
  )

  return { ...render(ui, { wrapper }), queryClient }
}
```

- [ ] **Step 9: Implement `specTest`**

Create `FE/src/test/specTest.ts` (design D5 — the claim rides in the title):

```ts
import { test } from 'vitest'

type TestFn = () => void | Promise<void>

function title(scenarioId: string, name: string): string {
  return `@spec:${scenarioId} ${name}`
}

/**
 * Declares that this test verifies a scenario from openspec/specs.
 * The identifier is embedded in the test title, which is where
 * tools/spec-coverage reads it from.
 */
export function specTest(scenarioId: string, name: string, fn: TestFn): void {
  test(title(scenarioId, name), fn)
}

specTest.skip = (scenarioId: string, name: string, fn: TestFn): void => {
  test.skip(title(scenarioId, name), fn)
}

specTest.only = (scenarioId: string, name: string, fn: TestFn): void => {
  test.only(title(scenarioId, name), fn)
}
```

- [ ] **Step 10: Run the harness test to verify it passes**

Run: `cd FE && npm test`
Expected: 1 passed.

- [ ] **Step 11: Commit**

```bash
git add FE/package.json FE/package-lock.json FE/vite.config.ts FE/src/test
git commit -m "test: add frontend vitest environment with MSW and provider harness"
```

---

### Task 6: Fake hub transport

Deliverable: a stand-in for the SignalR client that lets a test drive any server→client event from `FE/src/realtime/events.ts` and assert what the client invoked. Built now because 13 of the 14 remaining capabilities need it, and the harness-first decomposition exists precisely to settle its shape before those changes start.

**Files:**
- Create: `FE/src/test/fakeHub.ts`
- Test: `FE/src/test/fakeHub.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `createFakeHub()` → `FakeHub`
  - `FakeHub` — `on(event, handler)`, `off(event, handler)`, `invoke(method, ...args)` → `Promise<unknown>`, `emit(event, payload)`, `invocations: Array<{ method: string; args: unknown[] }>`, `handlerCount(event)` → `number`, `setInvokeResult(method, result)`, `setInvokeError(method, error)`

- [ ] **Step 1: Read the real client's shape**

Read `FE/src/realtime/hubClient.ts` and `FE/src/realtime/events.ts`. The fake must expose the same surface the app uses to register handlers and invoke methods; if the real module's exported names differ from `on`/`off`/`invoke`, mirror the real names instead and adjust the tests below to match.

- [ ] **Step 2: Write the failing test**

Create `FE/src/test/fakeHub.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest'
import { createFakeHub } from './fakeHub'

describe('fakeHub', () => {
  it('delivers an emitted event to every registered handler', () => {
    const hub = createFakeHub()
    const first = vi.fn()
    const second = vi.fn()
    hub.on('MessageReceived', first)
    hub.on('MessageReceived', second)

    hub.emit('MessageReceived', { id: 'm1', text: 'hello' })

    expect(first).toHaveBeenCalledWith({ id: 'm1', text: 'hello' })
    expect(second).toHaveBeenCalledWith({ id: 'm1', text: 'hello' })
  })

  it('stops delivering after off', () => {
    const hub = createFakeHub()
    const handler = vi.fn()
    hub.on('PresenceChanged', handler)
    hub.off('PresenceChanged', handler)

    hub.emit('PresenceChanged', { userId: 'u1', status: 'online' })

    expect(handler).not.toHaveBeenCalled()
    expect(hub.handlerCount('PresenceChanged')).toBe(0)
  })

  it('records invocations and returns the configured result', async () => {
    const hub = createFakeHub()
    hub.setInvokeResult('GetPresenceFor', { u1: 'online' })

    const result = await hub.invoke('GetPresenceFor', ['u1'])

    expect(result).toEqual({ u1: 'online' })
    expect(hub.invocations).toEqual([{ method: 'GetPresenceFor', args: [['u1']] }])
  })

  it('rejects when an invocation is configured to fail', async () => {
    const hub = createFakeHub()
    hub.setInvokeError('SendMessage', new Error('not a member'))

    await expect(hub.invoke('SendMessage', 'room-1', 'hi', null)).rejects.toThrow('not a member')
  })
})
```

- [ ] **Step 3: Run it to verify it fails**

Run: `cd FE && npx vitest run src/test/fakeHub.test.ts`
Expected: FAIL — cannot resolve `./fakeHub`.

- [ ] **Step 4: Implement the fake hub**

Create `FE/src/test/fakeHub.ts`:

```ts
type Handler = (payload: never) => void

export interface FakeHub {
  on(event: string, handler: Handler): void
  off(event: string, handler: Handler): void
  invoke(method: string, ...args: unknown[]): Promise<unknown>
  emit(event: string, payload: unknown): void
  handlerCount(event: string): number
  setInvokeResult(method: string, result: unknown): void
  setInvokeError(method: string, error: Error): void
  invocations: Array<{ method: string; args: unknown[] }>
}

/**
 * Stands in for the SignalR connection so realtime handlers can be driven
 * directly. Emitting is synchronous: the test controls exactly when the
 * server event lands, which removes any need to wait for a transport.
 */
export function createFakeHub(): FakeHub {
  const handlers = new Map<string, Set<Handler>>()
  const results = new Map<string, unknown>()
  const errors = new Map<string, Error>()
  const invocations: Array<{ method: string; args: unknown[] }> = []

  return {
    invocations,

    on(event, handler) {
      const set = handlers.get(event) ?? new Set<Handler>()
      set.add(handler)
      handlers.set(event, set)
    },

    off(event, handler) {
      handlers.get(event)?.delete(handler)
    },

    emit(event, payload) {
      for (const handler of handlers.get(event) ?? []) {
        ;(handler as (p: unknown) => void)(payload)
      }
    },

    handlerCount(event) {
      return handlers.get(event)?.size ?? 0
    },

    invoke(method, ...args) {
      invocations.push({ method, args })
      const error = errors.get(method)
      if (error) return Promise.reject(error)
      return Promise.resolve(results.get(method))
    },

    setInvokeResult(method, result) {
      results.set(method, result)
      errors.delete(method)
    },

    setInvokeError(method, error) {
      errors.set(method, error)
      results.delete(method)
    },
  }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd FE && npx vitest run src/test/fakeHub.test.ts`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add FE/src/test/fakeHub.ts FE/src/test/fakeHub.test.ts
git commit -m "test: add fake SignalR transport for frontend tests"
```

---

### Task 7: `user-sessions` frontend tests

Deliverable: the client-side `user-sessions` scenarios verified against real components and hooks.

**Files:**
- Create: `FE/src/hooks/useAuth.test.tsx`
- Create: `FE/src/features/sessions/SessionsPage.test.tsx`
- Delete: `FE/src/test/harness.test.tsx` (replaced by real coverage)

**Interfaces:**
- Consumes: `renderWithProviders`, `createTestQueryClient`, `server`, `specTest` (Task 5).
- Produces: nothing consumed by later tasks.

**Scenario identifiers claimed here:**
- `user-sessions/session-registration-per-browser/new-sign-in-registers-a-session`
- `user-sessions/session-registration-per-browser/restored-login-without-a-session`
- `user-sessions/session-registration-per-browser/session-registration-failure-is-non-fatal`
- `user-sessions/listing-active-sessions/viewing-sessions`

- [ ] **Step 1: Write the failing `useAuth` tests**

Create `FE/src/hooks/useAuth.test.tsx`. `useAuth` calls `useNavigate` and `useQueryClient`, so it needs both providers — that is what `renderWithProviders`' wrapper supplies to `renderHook`.

```tsx
import type { ReactNode } from 'react'
import { renderHook, act, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { http, HttpResponse } from 'msw'
import { expect } from 'vitest'
import { useAuth } from '@/hooks/useAuth'
import { useAuthStore } from '@/stores/authStore'
import { listSessions } from '@/api/sessions'
import { createTestQueryClient } from '@/test/renderWithProviders'
import { server } from '@/test/server'
import { specTest } from '@/test/specTest'

function wrapper({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={createTestQueryClient()}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  )
}

specTest(
  'user-sessions/session-registration-per-browser/new-sign-in-registers-a-session',
  'signing in with no stored session registers one and persists its id',
  async () => {
    expect(localStorage.getItem('sessionId')).toBeNull()

    const { result } = renderHook(() => useAuth(), { wrapper })
    await act(async () => {
      await result.current.login('tester@example.test', 'Passw0rd', false)
    })

    expect(localStorage.getItem('sessionId')).toBe('11111111-1111-1111-1111-111111111111')
  }
)

specTest(
  'user-sessions/session-registration-per-browser/new-sign-in-registers-a-session',
  'the registered session id is sent on later API requests',
  async () => {
    let sentSessionId: string | null = null
    server.use(
      http.get('/api/Sessions', ({ request }) => {
        sentSessionId = request.headers.get('X-Session-Id')
        return HttpResponse.json([])
      })
    )

    const { result } = renderHook(() => useAuth(), { wrapper })
    await act(async () => {
      await result.current.login('tester@example.test', 'Passw0rd', false)
    })
    await listSessions()

    expect(sentSessionId).toBe('11111111-1111-1111-1111-111111111111')
  }
)

specTest(
  'user-sessions/session-registration-per-browser/restored-login-without-a-session',
  'restoring a stored login with no session id registers a new session',
  async () => {
    sessionStorage.setItem('accessToken', 'stored-access')
    sessionStorage.setItem('refreshToken', 'stored-refresh')
    expect(localStorage.getItem('sessionId')).toBeNull()

    const { result } = renderHook(() => useAuth(), { wrapper })
    let restored = false
    await act(async () => {
      restored = await result.current.bootstrapAuth()
    })

    expect(restored).toBe(true)
    await waitFor(() =>
      expect(localStorage.getItem('sessionId')).toBe('11111111-1111-1111-1111-111111111111')
    )
  }
)

specTest(
  'user-sessions/session-registration-per-browser/session-registration-failure-is-non-fatal',
  'sign-in still completes when session registration fails',
  async () => {
    server.use(
      http.post('/api/Sessions/register', () => new HttpResponse(null, { status: 500 }))
    )

    const { result } = renderHook(() => useAuth(), { wrapper })
    await act(async () => {
      await result.current.login('tester@example.test', 'Passw0rd', false)
    })

    expect(useAuthStore.getState().accessToken).toBe('test-access')
    expect(useAuthStore.getState().me).not.toBeNull()
    expect(localStorage.getItem('sessionId')).toBeNull()
  }
)
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd FE && npx vitest run src/hooks/useAuth.test.tsx`
Expected: FAIL. If a failure is caused by a mismatched MSW handler path (for example `/api/Me` vs the real path in `FE/src/api/me.ts`), correct the handler in `FE/src/test/server.ts` — the source is authoritative — and re-run.

- [ ] **Step 3: Run them again to verify they pass**

Run: `cd FE && npx vitest run src/hooks/useAuth.test.tsx`
Expected: 4 passed. No production code changes were needed; these tests describe behavior that already exists.

- [ ] **Step 4: Write the failing `SessionsPage` test**

Create `FE/src/features/sessions/SessionsPage.test.tsx`:

```tsx
import { screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { expect } from 'vitest'
import SessionsPage from './SessionsPage'
import { renderWithProviders } from '@/test/renderWithProviders'
import { server } from '@/test/server'
import { specTest } from '@/test/specTest'

const sessions = [
  {
    id: 'aaaaaaaa-0000-0000-0000-000000000001',
    deviceInfo: 'This Laptop',
    userAgent: 'Mozilla/5.0 (Test)',
    ipAddress: '10.0.0.1',
    createdAt: '2026-01-02T10:00:00Z',
    lastSeenAt: '2026-01-02T12:00:00Z',
    isCurrent: true,
  },
  {
    id: 'aaaaaaaa-0000-0000-0000-000000000002',
    deviceInfo: 'Old Phone',
    userAgent: 'Mozilla/5.0 (Phone)',
    ipAddress: '10.0.0.2',
    createdAt: '2026-01-01T10:00:00Z',
    lastSeenAt: '2026-01-01T11:00:00Z',
    isCurrent: false,
  },
]

specTest(
  'user-sessions/listing-active-sessions/viewing-sessions',
  'lists every active session with its device details and marks the current one',
  async () => {
    server.use(http.get('/api/Sessions', () => HttpResponse.json(sessions)))

    renderWithProviders(<SessionsPage />, { accessToken: 'token-123' })

    await waitFor(() => expect(screen.getByText('This Laptop')).toBeInTheDocument())
    expect(screen.getByText('Old Phone')).toBeInTheDocument()
    expect(screen.getByText('Mozilla/5.0 (Test)')).toBeInTheDocument()
    expect(screen.getByText(/10\.0\.0\.1/)).toBeInTheDocument()

    // The "Current" badge sits inside the card whose title is "This Laptop".
    const currentCard = screen.getByText('This Laptop').closest('.card')!
    expect(currentCard).toHaveTextContent('Current')

    const otherCard = screen.getByText('Old Phone').closest('.card')!
    expect(otherCard).not.toHaveTextContent('Current')
    expect(otherCard).toHaveTextContent('Revoke')
  }
)
```

- [ ] **Step 5: Run it to verify it fails, then passes**

Run: `cd FE && npx vitest run src/features/sessions/SessionsPage.test.tsx`
Expected: passes on first run if the component already behaves as specified. If it fails, read the failure: an assertion mismatch against real rendered output means the test's expectation is wrong (fix the test); a genuine contradiction with the spec goes in `## Findings` per Global Constraints.

- [ ] **Step 6: Remove the scaffolding test**

```bash
git rm FE/src/test/harness.test.tsx
```

- [ ] **Step 7: Run the whole frontend suite**

Run: `cd FE && npm test`
Expected: 9 passed (4 `useAuth` + 1 `SessionsPage` + 4 `fakeHub`).

- [ ] **Step 8: Commit**

```bash
git add FE/src/hooks/useAuth.test.tsx FE/src/features/sessions/SessionsPage.test.tsx
git commit -m "test: verify user-sessions client rules with vitest and MSW"
```

---

### Task 8: Playwright harness

Deliverable: a Playwright setup that fails clearly when the stack is down, signs a fresh user in, and can put two browser contexts on the same account — proven by one test.

**Files:**
- Modify: `FE/package.json` (dev dependency only; the `e2e` script was added in Task 5)
- Modify: `FE/tsconfig.json` or add `FE/e2e/tsconfig.json` if the e2e directory is excluded from the app's TypeScript project
- Create: `FE/playwright.config.ts`
- Create: `FE/e2e/globalSetup.ts`
- Create: `FE/e2e/fixtures.ts`
- Test: `FE/e2e/harness.spec.ts`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `test` — Playwright test extended with the `signedInUser` fixture
  - `expect` — re-exported from `@playwright/test`
  - `specTest(scenarioId, name, fn)` — Playwright-flavoured claim wrapper
  - `registerUser(page)` → `Promise<TestAccount>` where `TestAccount = { email: string; username: string; password: string }`
  - `signIn(page, account)` → `Promise<void>`
  - `openSecondContext(browser, account)` → `Promise<{ context: BrowserContext; page: Page }>`

- [ ] **Step 1: Install Playwright**

```bash
cd FE
npm install -D @playwright/test@1
npx playwright install chromium
```

- [ ] **Step 2: Write the config**

Create `FE/playwright.config.ts`. No `webServer` — the suite targets the compose stack the developer already runs (design D8).

```ts
import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  globalSetup: './e2e/globalSetup.ts',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [
    ['list'],
    ['json', { outputFile: 'playwright-report/results.json' }],
  ],
  use: {
    baseURL: 'http://localhost:3000',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
```

`fullyParallel: false` and `workers: 1` because these tests share one backend and one database; parallel workers would interleave presence and session state. Revisit only if the suite becomes slow.

- [ ] **Step 3: Write the global setup**

Create `FE/e2e/globalSetup.ts` (spec: "Missing prerequisite"):

```ts
export default async function globalSetup(): Promise<void> {
  const url = 'http://localhost:3000'
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(5000) })
    if (!response.ok) throw new Error(`status ${response.status}`)
  } catch (cause) {
    throw new Error(
      `The application stack is not reachable at ${url}.\n` +
        `End-to-end tests run against the running Docker Compose stack.\n` +
        `Start it first:  start.bat   (or: docker compose up -d --build)\n` +
        `Underlying error: ${String(cause)}`
    )
  }
}
```

- [ ] **Step 4: Write the failing harness test**

Create `FE/e2e/harness.spec.ts`:

```ts
import { expect, specTest, openSecondContext, test } from './fixtures'

specTest(
  'user-sessions/listing-active-sessions/viewing-sessions',
  'a signed-in user sees their own session listed',
  async ({ signedInUser }) => {
    const { page } = signedInUser

    await page.goto('/sessions')

    await expect(page.getByRole('heading', { name: 'Active Sessions' })).toBeVisible()
    await expect(page.getByText('Current')).toBeVisible()
  }
)

test('two contexts on the same account produce two sessions', async ({ signedInUser, browser }) => {
  const second = await openSecondContext(browser, signedInUser.account)

  await signedInUser.page.goto('/sessions')
  await expect(signedInUser.page.getByRole('button', { name: 'Revoke' })).toHaveCount(1)

  await second.context.close()
})
```

- [ ] **Step 5: Run it to verify it fails**

Run: `cd FE && npm run e2e`
Expected: FAIL — cannot resolve `./fixtures`.

- [ ] **Step 6: Implement the fixtures**

Create `FE/e2e/fixtures.ts`. Read `FE/src/features/auth/Register.tsx` and `SignIn.tsx` first and adjust the selectors below to match the real labels and button text — the components are authoritative.

```ts
import { test as base, expect, type Browser, type BrowserContext, type Page } from '@playwright/test'

export interface TestAccount {
  email: string
  username: string
  password: string
}

export const PASSWORD = 'Passw0rd'

function uniqueAccount(): TestAccount {
  const suffix = Math.random().toString(36).slice(2, 12)
  return {
    email: `e2e-${suffix}@example.test`,
    username: `e2e${suffix}`,
    password: PASSWORD,
  }
}

export async function registerUser(page: Page): Promise<TestAccount> {
  const account = uniqueAccount()

  await page.goto('/register')
  await page.getByLabel(/email/i).fill(account.email)
  await page.getByLabel(/user ?name/i).fill(account.username)
  await page.getByLabel(/^password/i).fill(account.password)

  const confirm = page.getByLabel(/confirm password/i)
  if (await confirm.count()) await confirm.fill(account.password)

  await page.getByRole('button', { name: /register|sign up|create account/i }).click()
  await page.waitForURL(/\/login|\/rooms/)

  return account
}

export async function signIn(page: Page, account: TestAccount): Promise<void> {
  await page.goto('/login')
  await page.getByLabel(/email/i).fill(account.email)
  await page.getByLabel(/password/i).fill(account.password)
  await page.getByRole('button', { name: /sign in|log in/i }).click()
  await page.waitForURL(/\/rooms/)
}

/** A second browser context signed in as the same account — a second "browser". */
export async function openSecondContext(
  browser: Browser,
  account: TestAccount
): Promise<{ context: BrowserContext; page: Page }> {
  const context = await browser.newContext({ baseURL: 'http://localhost:3000' })
  const page = await context.newPage()
  await signIn(page, account)
  return { context, page }
}

interface Fixtures {
  signedInUser: { page: Page; account: TestAccount }
}

export const test = base.extend<Fixtures>({
  signedInUser: async ({ page }, use) => {
    const account = await registerUser(page)
    await signIn(page, account)
    await use({ page, account })
  },
})

export { expect }

type SpecFn = Parameters<typeof test>[1]

/** Declares that this test verifies a scenario from openspec/specs. */
export function specTest(scenarioId: string, name: string, fn: SpecFn): void {
  test(`@spec:${scenarioId} ${name}`, fn)
}
```

- [ ] **Step 7: Bring the stack up and run the tests**

```bash
cd c:/Projects/andrey-chat
start.bat
```

Wait for `http://localhost:3000` to answer, then:

Run: `cd FE && npm run e2e`
Expected: 2 passed. If a selector does not match, fix the selector in `fixtures.ts` against the real component markup and re-run — do not change the components.

- [ ] **Step 8: Verify the missing-prerequisite path**

```bash
docker compose stop fe
cd FE && npm run e2e
```

Expected: fails during global setup with "The application stack is not reachable at http://localhost:3000" and "start.bat".

Restart: `docker compose start fe`

- [ ] **Step 9: Commit**

```bash
git add FE/package.json FE/package-lock.json FE/playwright.config.ts FE/e2e
git commit -m "test: add Playwright harness targeting the compose stack"
```

---

### Task 9: `user-sessions` end-to-end tests

Deliverable: the two `user-sessions` scenarios that only the assembled system can demonstrate.

**Files:**
- Create: `FE/e2e/sessions.spec.ts`
- Delete: `FE/e2e/harness.spec.ts` (replaced by real coverage)

**Interfaces:**
- Consumes: `test`, `expect`, `specTest`, `openSecondContext` (Task 8).
- Produces: nothing consumed by later tasks.

**Scenario identifiers claimed here:**
- `user-sessions/revoking-sessions/revoking-another-session`
- `user-sessions/revoking-sessions/revoking-the-current-session`

- [ ] **Step 1: Write the failing tests**

Create `FE/e2e/sessions.spec.ts`:

```ts
import { expect, openSecondContext, specTest } from './fixtures'

specTest(
  'user-sessions/revoking-sessions/revoking-another-session',
  'revoking a session other than the current one removes its row and keeps the browser signed in',
  async ({ signedInUser, browser }) => {
    const { page, account } = signedInUser
    const second = await openSecondContext(browser, account)

    await page.goto('/sessions')
    // Two sessions exist: this browser (marked Current) and the second context.
    await expect(page.getByRole('button', { name: 'Revoke' })).toHaveCount(1)

    await page.getByRole('button', { name: 'Revoke' }).click()

    await expect(page.getByRole('button', { name: 'Revoke' })).toHaveCount(0)
    await expect(page.getByText('Current')).toBeVisible()
    await expect(page).toHaveURL(/\/sessions/)

    await second.context.close()
  }
)

specTest(
  'user-sessions/revoking-sessions/revoking-the-current-session',
  'revoking the current session clears credentials and returns to sign-in',
  async ({ signedInUser }) => {
    const { page } = signedInUser

    await page.goto('/sessions')
    await expect(page.getByText('Current')).toBeVisible()

    await page.getByRole('button', { name: 'Sign out' }).click()

    await expect(page).toHaveURL(/\/login/)
    expect(await page.evaluate(() => localStorage.getItem('sessionId'))).toBeNull()
    expect(await page.evaluate(() => sessionStorage.getItem('accessToken'))).toBeNull()
  }
)
```

- [ ] **Step 2: Run them to verify they fail or pass honestly**

Run: `cd FE && npm run e2e`
Expected: both run against the live stack. Read any failure carefully — `SessionsPage` renders "Sign out" for the current session and "Revoke" for others, so a count mismatch means the session count is not what the test assumed, not that the button labels are wrong.

- [ ] **Step 3: Remove the scaffolding test**

```bash
git rm FE/e2e/harness.spec.ts
```

- [ ] **Step 4: Run the e2e suite to verify it passes**

Run: `cd FE && npm run e2e`
Expected: 2 passed.

- [ ] **Step 5: Commit**

```bash
git add FE/e2e/sessions.spec.ts
git commit -m "test: verify session revocation end to end across two browser contexts"
```

---

### Task 10: Spec parser

Deliverable: a tool that turns the 15 capability specs into the canonical list of scenario identifiers.

**Files:**
- Create: `tools/spec-coverage/package.json`
- Create: `tools/spec-coverage/parseSpecs.js`
- Test: `tools/spec-coverage/parseSpecs.test.js`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `slugify(text)` → `string`
  - `parseSpecFile(markdown, capability)` → `Array<{ id, capability, requirement, scenario }>`
  - `parseAllSpecs(specsDir)` → same array across every `<capability>/spec.md`

- [ ] **Step 1: Create the package**

Create `tools/spec-coverage/package.json`:

```json
{
  "name": "spec-coverage",
  "private": true,
  "type": "module",
  "version": "1.0.0",
  "description": "Cross-references openspec scenarios against automated test claims.",
  "scripts": {
    "test": "node --test"
  }
}
```

No dependencies — the tool must run without any install step (design D6).

- [ ] **Step 2: Write the failing test**

Create `tools/spec-coverage/parseSpecs.test.js`:

```js
import { strict as assert } from 'node:assert'
import { test } from 'node:test'
import { parseSpecFile, slugify } from './parseSpecs.js'

test('slugify lowercases and collapses non-alphanumerics', () => {
  assert.equal(slugify('Revoking a foreign session'), 'revoking-a-foreign-session')
  assert.equal(slugify('Multiline and emoji'), 'multiline-and-emoji')
  assert.equal(slugify('  Session registration failure is non-fatal  '), 'session-registration-failure-is-non-fatal')
  assert.equal(slugify('Reply target in another room'), 'reply-target-in-another-room')
})

test('parseSpecFile pairs every scenario with its requirement', () => {
  const markdown = [
    '# User Sessions Specification',
    '',
    '## Purpose',
    '',
    'Some purpose text.',
    '',
    '## Requirements',
    '',
    '### Requirement: Listing active sessions',
    'Text about listing.',
    '',
    '#### Scenario: Viewing sessions',
    '- **WHEN** a user opens the sessions screen',
    '- **THEN** every active session is listed',
    '',
    '#### Scenario: Only own sessions',
    '- **WHEN** a user lists sessions',
    '- **THEN** no other account is returned',
    '',
    '### Requirement: Revoking sessions',
    'Text about revoking.',
    '',
    '#### Scenario: Revoking a foreign session',
    '- **WHEN** a revoke names another user session',
    '- **THEN** it is rejected',
  ].join('\n')

  const scenarios = parseSpecFile(markdown, 'user-sessions')

  assert.equal(scenarios.length, 3)
  assert.deepEqual(scenarios[0], {
    id: 'user-sessions/listing-active-sessions/viewing-sessions',
    capability: 'user-sessions',
    requirement: 'Listing active sessions',
    scenario: 'Viewing sessions',
  })
  assert.equal(scenarios[2].id, 'user-sessions/revoking-sessions/revoking-a-foreign-session')
})

test('parseSpecFile ignores headings that are not requirements or scenarios', () => {
  const markdown = '## Purpose\n\ntext\n\n### Not a requirement\n\n#### Not a scenario\n'
  assert.deepEqual(parseSpecFile(markdown, 'x'), [])
})

test('parseSpecFile throws when a scenario precedes any requirement', () => {
  const markdown = '#### Scenario: Orphan\n- **WHEN** x\n'
  assert.throws(() => parseSpecFile(markdown, 'x'), /before any requirement/)
})
```

- [ ] **Step 3: Run it to verify it fails**

Run: `cd tools/spec-coverage && node --test parseSpecs.test.js`
Expected: FAIL — cannot find module `./parseSpecs.js`.

- [ ] **Step 4: Implement the parser**

Create `tools/spec-coverage/parseSpecs.js`:

```js
import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'

/** Global Constraints: lowercase, collapse non-alphanumerics to '-', trim. */
export function slugify(text) {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

const REQUIREMENT = /^###\s+Requirement:\s*(.+?)\s*$/
const SCENARIO = /^####\s+Scenario:\s*(.+?)\s*$/

export function parseSpecFile(markdown, capability) {
  const scenarios = []
  let requirement = null

  for (const line of markdown.split(/\r?\n/)) {
    const requirementMatch = REQUIREMENT.exec(line)
    if (requirementMatch) {
      requirement = requirementMatch[1]
      continue
    }

    const scenarioMatch = SCENARIO.exec(line)
    if (!scenarioMatch) continue

    if (requirement === null) {
      throw new Error(
        `${capability}: scenario "${scenarioMatch[1]}" appears before any requirement heading.`
      )
    }

    scenarios.push({
      id: `${capability}/${slugify(requirement)}/${slugify(scenarioMatch[1])}`,
      capability,
      requirement,
      scenario: scenarioMatch[1],
    })
  }

  return scenarios
}

export async function parseAllSpecs(specsDir) {
  const entries = await readdir(specsDir, { withFileTypes: true })
  const capabilities = entries
    .filter((entry) => entry.isDirectory() && entry.name !== 'archive')
    .map((entry) => entry.name)
    .sort()

  const all = []
  for (const capability of capabilities) {
    const path = join(specsDir, capability, 'spec.md')
    let markdown
    try {
      markdown = await readFile(path, 'utf8')
    } catch {
      continue // a capability directory without a spec.md is not an error
    }
    all.push(...parseSpecFile(markdown, capability))
  }

  const duplicates = all
    .map((s) => s.id)
    .filter((id, index, ids) => ids.indexOf(id) !== index)
  if (duplicates.length) {
    throw new Error(`Duplicate scenario identifiers in specs: ${[...new Set(duplicates)].join(', ')}`)
  }

  return all
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd tools/spec-coverage && node --test parseSpecs.test.js`
Expected: 4 passed.

- [ ] **Step 6: Verify against the real specs**

```bash
cd c:/Projects/andrey-chat
node -e "import('./tools/spec-coverage/parseSpecs.js').then(async m => { const s = await m.parseAllSpecs('openspec/specs'); console.log('total', s.length); const byCap = {}; for (const x of s) byCap[x.capability] = (byCap[x.capability]||0)+1; console.table(byCap) })"
```

Expected: `total 270` across 15 capabilities, matching the counts in `proposal.md`. If the total differs, the specs changed since the proposal was written — verify by hand which capability moved and note it in the commit message.

- [ ] **Step 7: Commit**

```bash
git add tools/spec-coverage/package.json tools/spec-coverage/parseSpecs.js tools/spec-coverage/parseSpecs.test.js
git commit -m "test: parse openspec scenarios into stable identifiers"
```

---

### Task 11: Result readers and the coverage report

Deliverable: `node tools/spec-coverage` reads all three runners' output, writes the scenario report, and exits non-zero on a stale claim.

**Files:**
- Create: `tools/spec-coverage/readResults.js`
- Create: `tools/spec-coverage/report.js`
- Create: `tools/spec-coverage/index.js`
- Test: `tools/spec-coverage/readResults.test.js`
- Test: `tools/spec-coverage/report.test.js`

**Interfaces:**
- Consumes: `parseAllSpecs` (Task 10).
- Produces:
  - `extractScenarioIds(testName)` → `string[]`
  - `readTrx(xml)` / `readVitest(json)` / `readPlaywright(json)` → `Array<Claim>` where `Claim = { layer, testName, status, scenarioIds }` and `status` is `'passed' | 'failed' | 'skipped'`
  - `buildReport(scenarios, claims)` → `{ covered, uncovered, unknown, byCapability, total, coveredCount, percent }`
  - `renderMarkdown(report)` → `string`

- [ ] **Step 1: Write the failing reader test**

Create `tools/spec-coverage/readResults.test.js`:

```js
import { strict as assert } from 'node:assert'
import { test } from 'node:test'
import { extractScenarioIds, readPlaywright, readTrx, readVitest } from './readResults.js'

test('extractScenarioIds finds every marker in a title', () => {
  assert.deepEqual(extractScenarioIds('@spec:a/b/c does a thing'), ['a/b/c'])
  assert.deepEqual(
    extractScenarioIds('@spec:a/b/c @spec:d/e/f covers two scenarios'),
    ['a/b/c', 'd/e/f']
  )
  assert.deepEqual(extractScenarioIds('an ordinary test with no claim'), [])
})

test('readTrx pairs test names with outcomes', () => {
  const xml = `<?xml version="1.0"?>
  <TestRun>
    <Results>
      <UnitTestResult testName="@spec:a/b/c passes" outcome="Passed" duration="00:00:01" />
      <UnitTestResult testName="@spec:d/e/f fails" outcome="Failed" />
      <UnitTestResult testName="@spec:g/h/i skipped" outcome="NotExecuted" />
      <UnitTestResult testName="unclaimed test" outcome="Passed" />
    </Results>
  </TestRun>`

  const claims = readTrx(xml)

  assert.equal(claims.length, 4)
  assert.deepEqual(claims[0], {
    layer: 'backend',
    testName: '@spec:a/b/c passes',
    status: 'passed',
    scenarioIds: ['a/b/c'],
  })
  assert.equal(claims[1].status, 'failed')
  assert.equal(claims[2].status, 'skipped')
  assert.deepEqual(claims[3].scenarioIds, [])
})

test('readVitest reads assertion results', () => {
  const json = {
    testResults: [
      {
        assertionResults: [
          { fullName: '@spec:a/b/c does a thing', status: 'passed' },
          { fullName: '@spec:d/e/f broken', status: 'failed' },
          { fullName: '@spec:g/h/i pending', status: 'skipped' },
        ],
      },
    ],
  }

  const claims = readVitest(json)

  assert.equal(claims.length, 3)
  assert.equal(claims[0].layer, 'frontend')
  assert.deepEqual(claims[0].scenarioIds, ['a/b/c'])
  assert.equal(claims[1].status, 'failed')
})

test('readPlaywright walks nested suites', () => {
  const json = {
    suites: [
      {
        suites: [
          {
            specs: [
              { title: '@spec:a/b/c nested', tests: [{ results: [{ status: 'passed' }] }] },
            ],
          },
        ],
        specs: [
          { title: '@spec:d/e/f top level', tests: [{ results: [{ status: 'failed' }] }] },
        ],
      },
    ],
  }

  const claims = readPlaywright(json)

  assert.equal(claims.length, 2)
  assert.deepEqual(claims.map((c) => c.status).sort(), ['failed', 'passed'])
  assert.ok(claims.every((c) => c.layer === 'e2e'))
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd tools/spec-coverage && node --test readResults.test.js`
Expected: FAIL — cannot find module `./readResults.js`.

- [ ] **Step 3: Implement the readers**

Create `tools/spec-coverage/readResults.js`. TRX is parsed with a regex rather than an XML library so the tool stays dependency-free; both attributes live on the same self-describing element, which makes this safe.

```js
import { readFile } from 'node:fs/promises'

const MARKER = /@spec:([a-z0-9-]+\/[a-z0-9-]+\/[a-z0-9-]+)/g

export function extractScenarioIds(testName) {
  return [...testName.matchAll(MARKER)].map((match) => match[1])
}

const TRX_RESULT = /<UnitTestResult\b[^>]*>/g
const TRX_NAME = /\btestName="([^"]*)"/
const TRX_OUTCOME = /\boutcome="([^"]*)"/

function decodeXml(text) {
  return text
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&')
}

function normalizeTrxOutcome(outcome) {
  if (outcome === 'Passed') return 'passed'
  if (outcome === 'Failed' || outcome === 'Error' || outcome === 'Timeout') return 'failed'
  return 'skipped'
}

export function readTrx(xml) {
  const claims = []
  for (const element of xml.match(TRX_RESULT) ?? []) {
    const name = TRX_NAME.exec(element)
    if (!name) continue
    const outcome = TRX_OUTCOME.exec(element)
    const testName = decodeXml(name[1])
    claims.push({
      layer: 'backend',
      testName,
      status: normalizeTrxOutcome(outcome ? outcome[1] : 'NotExecuted'),
      scenarioIds: extractScenarioIds(testName),
    })
  }
  return claims
}

function normalizeStatus(status) {
  if (status === 'passed' || status === 'expected') return 'passed'
  if (status === 'failed' || status === 'unexpected' || status === 'timedOut') return 'failed'
  return 'skipped'
}

export function readVitest(json) {
  const claims = []
  for (const file of json.testResults ?? []) {
    for (const assertion of file.assertionResults ?? []) {
      const testName = assertion.fullName ?? assertion.title ?? ''
      claims.push({
        layer: 'frontend',
        testName,
        status: normalizeStatus(assertion.status),
        scenarioIds: extractScenarioIds(testName),
      })
    }
  }
  return claims
}

export function readPlaywright(json) {
  const claims = []

  const walk = (suite) => {
    for (const spec of suite.specs ?? []) {
      const results = (spec.tests ?? []).flatMap((t) => t.results ?? [])
      const statuses = results.map((r) => normalizeStatus(r.status))
      const status = statuses.includes('failed')
        ? 'failed'
        : statuses.includes('passed')
          ? 'passed'
          : 'skipped'
      claims.push({
        layer: 'e2e',
        testName: spec.title ?? '',
        status,
        scenarioIds: extractScenarioIds(spec.title ?? ''),
      })
    }
    for (const child of suite.suites ?? []) walk(child)
  }

  for (const suite of json.suites ?? []) walk(suite)
  return claims
}

/** Reads a result file if present; a missing file means that layer did not run. */
export async function readIfPresent(path, parse) {
  try {
    const content = await readFile(path, 'utf8')
    return parse(content)
  } catch (error) {
    if (error.code === 'ENOENT') return null
    throw error
  }
}
```

- [ ] **Step 4: Run the reader tests to verify they pass**

Run: `cd tools/spec-coverage && node --test readResults.test.js`
Expected: 4 passed.

- [ ] **Step 5: Write the failing report test**

Create `tools/spec-coverage/report.test.js`:

```js
import { strict as assert } from 'node:assert'
import { test } from 'node:test'
import { buildReport, renderMarkdown } from './report.js'

const scenarios = [
  { id: 'cap/req/one', capability: 'cap', requirement: 'Req', scenario: 'One' },
  { id: 'cap/req/two', capability: 'cap', requirement: 'Req', scenario: 'Two' },
  { id: 'other/req/three', capability: 'other', requirement: 'Req', scenario: 'Three' },
]

test('a passing claim covers its scenario', () => {
  const report = buildReport(scenarios, [
    { layer: 'backend', testName: 't1', status: 'passed', scenarioIds: ['cap/req/one'] },
  ])

  assert.deepEqual(report.covered.get('cap/req/one'), [{ layer: 'backend', testName: 't1' }])
  assert.equal(report.coveredCount, 1)
  assert.equal(report.total, 3)
})

test('failed and skipped claims do not cover', () => {
  const report = buildReport(scenarios, [
    { layer: 'backend', testName: 'f', status: 'failed', scenarioIds: ['cap/req/one'] },
    { layer: 'frontend', testName: 's', status: 'skipped', scenarioIds: ['cap/req/two'] },
  ])

  assert.equal(report.coveredCount, 0)
  assert.deepEqual(report.uncovered.map((s) => s.id).sort(), [
    'cap/req/one',
    'cap/req/two',
    'other/req/three',
  ])
})

test('an unrecognised identifier is reported as unknown', () => {
  const report = buildReport(scenarios, [
    { layer: 'e2e', testName: 'stale', status: 'passed', scenarioIds: ['cap/req/typo'] },
  ])

  assert.deepEqual(report.unknown, [
    { scenarioId: 'cap/req/typo', layer: 'e2e', testName: 'stale' },
  ])
})

test('per-capability counts are reported', () => {
  const report = buildReport(scenarios, [
    { layer: 'backend', testName: 't', status: 'passed', scenarioIds: ['cap/req/one', 'cap/req/two'] },
  ])

  assert.deepEqual(report.byCapability.get('cap'), { total: 2, covered: 2 })
  assert.deepEqual(report.byCapability.get('other'), { total: 1, covered: 0 })
})

test('markdown lists uncovered scenarios under their capability', () => {
  const report = buildReport(scenarios, [
    { layer: 'backend', testName: 't', status: 'passed', scenarioIds: ['cap/req/one'] },
  ])

  const markdown = renderMarkdown(report)

  assert.match(markdown, /cap\/req\/two/)
  assert.match(markdown, /other\/req\/three/)
  assert.doesNotMatch(markdown, /^- `cap\/req\/one`/m)
})
```

- [ ] **Step 6: Run it to verify it fails**

Run: `cd tools/spec-coverage && node --test report.test.js`
Expected: FAIL — cannot find module `./report.js`.

- [ ] **Step 7: Implement the report**

Create `tools/spec-coverage/report.js`:

```js
export function buildReport(scenarios, claims) {
  const known = new Map(scenarios.map((s) => [s.id, s]))
  const covered = new Map()
  const unknown = []

  for (const claim of claims) {
    for (const scenarioId of claim.scenarioIds) {
      if (!known.has(scenarioId)) {
        unknown.push({ scenarioId, layer: claim.layer, testName: claim.testName })
        continue
      }
      // Spec: a failing or skipped test does not count as covering.
      if (claim.status !== 'passed') continue

      const entries = covered.get(scenarioId) ?? []
      entries.push({ layer: claim.layer, testName: claim.testName })
      covered.set(scenarioId, entries)
    }
  }

  const uncovered = scenarios.filter((s) => !covered.has(s.id))

  const byCapability = new Map()
  for (const scenario of scenarios) {
    const entry = byCapability.get(scenario.capability) ?? { total: 0, covered: 0 }
    entry.total += 1
    if (covered.has(scenario.id)) entry.covered += 1
    byCapability.set(scenario.capability, entry)
  }

  const total = scenarios.length
  const coveredCount = covered.size

  return {
    covered,
    uncovered,
    unknown,
    byCapability,
    total,
    coveredCount,
    percent: total === 0 ? 0 : Math.round((coveredCount / total) * 1000) / 10,
  }
}

export function renderMarkdown(report) {
  const lines = []
  lines.push('# Scenario Coverage', '')
  lines.push(
    `**${report.coveredCount} of ${report.total} specified scenarios covered (${report.percent}%).**`,
    ''
  )
  lines.push(
    'Scenario coverage is tracked separately from line coverage: it says what has a test,',
    'not how much code those tests reach.',
    ''
  )

  lines.push('## By capability', '')
  lines.push('| Capability | Covered | Total | % |')
  lines.push('|---|---:|---:|---:|')
  for (const [capability, { total, covered }] of [...report.byCapability].sort()) {
    const percent = total === 0 ? 0 : Math.round((covered / total) * 1000) / 10
    lines.push(`| \`${capability}\` | ${covered} | ${total} | ${percent}% |`)
  }
  lines.push('')

  if (report.unknown.length) {
    lines.push('## Stale claims', '')
    lines.push('These tests claim scenario identifiers that do not exist in the specs:', '')
    for (const { scenarioId, layer, testName } of report.unknown) {
      lines.push(`- \`${scenarioId}\` claimed by ${layer} test \`${testName}\``)
    }
    lines.push('')
  }

  lines.push('## Uncovered scenarios', '')
  if (report.uncovered.length === 0) {
    lines.push('None — every specified scenario has a passing test.', '')
  } else {
    let currentCapability = null
    for (const scenario of report.uncovered) {
      if (scenario.capability !== currentCapability) {
        currentCapability = scenario.capability
        lines.push('', `### ${currentCapability}`, '')
      }
      lines.push(`- \`${scenario.id}\` — ${scenario.requirement} / ${scenario.scenario}`)
    }
    lines.push('')
  }

  lines.push('## Covered scenarios', '')
  for (const [scenarioId, entries] of [...report.covered].sort()) {
    const by = entries.map((e) => `${e.layer}: \`${e.testName}\``).join(', ')
    lines.push(`- \`${scenarioId}\` — ${by}`)
  }
  lines.push('')

  return lines.join('\n')
}
```

- [ ] **Step 8: Run the report tests to verify they pass**

Run: `cd tools/spec-coverage && node --test report.test.js`
Expected: 5 passed.

- [ ] **Step 9: Write the entry point**

Create `tools/spec-coverage/index.js`:

```js
import { mkdir, readdir, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseAllSpecs } from './parseSpecs.js'
import { readIfPresent, readPlaywright, readTrx, readVitest } from './readResults.js'
import { buildReport, renderMarkdown } from './report.js'

const here = dirname(fileURLToPath(import.meta.url))
const repoRoot = resolve(here, '..', '..')

const SPECS_DIR = join(repoRoot, 'openspec', 'specs')
const TRX_DIR = join(repoRoot, 'BE', 'Tests.Integration', 'TestResults')
const VITEST_JSON = join(repoRoot, 'FE', '.vitest-report.json')
const PLAYWRIGHT_JSON = join(repoRoot, 'FE', 'playwright-report', 'results.json')
const OUTPUT = join(repoRoot, 'docs', 'test-coverage', 'scenarios.md')

/** dotnet test writes a timestamped .trx; take the newest. */
async function newestTrxPath() {
  try {
    const entries = await readdir(TRX_DIR, { withFileTypes: true })
    const files = entries.filter((e) => e.isFile() && e.name.endsWith('.trx')).map((e) => e.name)
    if (files.length === 0) return null
    files.sort()
    return join(TRX_DIR, files[files.length - 1])
  } catch {
    return null
  }
}

const scenarios = await parseAllSpecs(SPECS_DIR)

const claims = []
const layersRun = []

const trxPath = await newestTrxPath()
if (trxPath) {
  // Parenthesise before spreading: `...a ?? []` binds the spread first.
  claims.push(...((await readIfPresent(trxPath, readTrx)) ?? []))
  layersRun.push('backend')
}

const vitest = await readIfPresent(VITEST_JSON, (text) => readVitest(JSON.parse(text)))
if (vitest) {
  claims.push(...vitest)
  layersRun.push('frontend')
}

const playwright = await readIfPresent(PLAYWRIGHT_JSON, (text) => readPlaywright(JSON.parse(text)))
if (playwright) {
  claims.push(...playwright)
  layersRun.push('e2e')
}

const report = buildReport(scenarios, claims)

await mkdir(dirname(OUTPUT), { recursive: true })
await writeFile(OUTPUT, renderMarkdown(report), 'utf8')

console.log('')
console.log('Scenario coverage')
console.log('-----------------')
console.log(`  layers read : ${layersRun.length ? layersRun.join(', ') : 'none'}`)
console.log(`  scenarios   : ${report.coveredCount}/${report.total} covered (${report.percent}%)`)
for (const [capability, { total, covered }] of [...report.byCapability].sort()) {
  if (covered > 0) console.log(`      ${capability}: ${covered}/${total}`)
}
console.log(`  report      : ${OUTPUT}`)

if (layersRun.length === 0) {
  console.error('\nNo test results found. Run the suites before generating the report.')
  process.exit(1)
}

if (report.unknown.length > 0) {
  console.error('\nStale scenario claims — these identifiers do not exist in the specs:')
  for (const { scenarioId, layer, testName } of report.unknown) {
    console.error(`  ${scenarioId}  (${layer}: ${testName})`)
  }
  console.error('Fix the claim or the spec, then re-run.')
  process.exit(1)
}

console.log('')
```

- [ ] **Step 10: Generate a real report**

With the backend, frontend, and e2e suites already run in earlier tasks:

```bash
cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --logger "trx" --settings Tests.Integration/coverlet.runsettings --collect:"XPlat Code Coverage"
cd ../FE && npm run test:coverage && npm run e2e
cd .. && node tools/spec-coverage
```

Expected: `user-sessions: 8/8` in the console summary, overall `8/270`, and `docs/test-coverage/scenarios.md` written.

- [ ] **Step 11: Verify stale-claim detection**

Temporarily change one identifier in `FE/src/features/sessions/SessionsPage.test.tsx` to `user-sessions/listing-active-sessions/viewing-sessionz`, then:

```bash
cd FE && npm run test:coverage
cd .. && node tools/spec-coverage
echo "exit code: $?"
```

Expected: exit code 1, with `user-sessions/listing-active-sessions/viewing-sessionz` named in the error output (spec: "Test claiming an unknown scenario").

Revert the typo and re-run to confirm it passes again.

- [ ] **Step 12: Commit**

```bash
git add tools/spec-coverage docs/test-coverage/scenarios.md
git commit -m "test: cross-reference spec scenarios against test claims"
```

---

### Task 12: One-command orchestration

Deliverable: `test.bat` runs every gated layer, reports both coverage numbers and scenario coverage, and names every report path (design D10).

**Files:**
- Create: `test.bat`
- Create: `tools/coverage-gate/package.json`
- Create: `tools/coverage-gate/index.js`
- Create: `docs/test-coverage/.gitkeep`

**Interfaces:**
- Consumes: everything above.
- Produces: `node tools/coverage-gate [--enforce]` — prints both line-coverage percentages against the 80% threshold; exits 1 on a shortfall only when `--enforce` is passed.

- [ ] **Step 1: Install the coverage report generator**

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

If it is already installed, `dotnet tool update -g dotnet-reportgenerator-globaltool` is fine.

- [ ] **Step 2: Write the coverage gate**

The 80% threshold has to be *evaluated* here even though it is not yet *enforced* — the spec requires the run to report the shortfall with the measured percentage. Deferring only the exit code makes flipping to enforcing a one-flag change.

Create `tools/coverage-gate/package.json`:

```json
{
  "name": "coverage-gate",
  "private": true,
  "type": "module",
  "version": "1.0.0",
  "description": "Compares measured line coverage against the 80% threshold."
}
```

Create `tools/coverage-gate/index.js`:

```js
import { readFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const THRESHOLD = 80

const here = dirname(fileURLToPath(import.meta.url))
const repoRoot = resolve(here, '..', '..')

const BACKEND_SUMMARY = join(repoRoot, 'docs', 'test-coverage', 'backend', 'Summary.txt')
const FRONTEND_SUMMARY = join(repoRoot, 'docs', 'test-coverage', 'frontend', 'coverage-summary.json')

const enforce = process.argv.includes('--enforce')

/** ReportGenerator's TextSummary contains a line like "Line coverage: 43.2%". */
async function backendPercent() {
  try {
    const text = await readFile(BACKEND_SUMMARY, 'utf8')
    const match = /Line coverage:\s*([\d.]+)%/.exec(text)
    return match ? Number(match[1]) : null
  } catch {
    return null
  }
}

/** vitest's json-summary reporter writes total.lines.pct. */
async function frontendPercent() {
  try {
    const json = JSON.parse(await readFile(FRONTEND_SUMMARY, 'utf8'))
    const pct = json?.total?.lines?.pct
    return typeof pct === 'number' ? pct : null
  } catch {
    return null
  }
}

const measurements = [
  { label: 'Backend line coverage ', percent: await backendPercent(), source: BACKEND_SUMMARY },
  { label: 'Frontend line coverage', percent: await frontendPercent(), source: FRONTEND_SUMMARY },
]

console.log('')
console.log(`Line coverage gate (threshold ${THRESHOLD}%)`)
console.log('------------------------------------------')

let shortfall = false
let missing = false

for (const { label, percent, source } of measurements) {
  if (percent === null) {
    missing = true
    console.log(`  ${label} : NOT MEASURED — no report at ${source}`)
    continue
  }
  const passed = percent >= THRESHOLD
  if (!passed) shortfall = true
  console.log(
    `  ${label} : ${percent.toFixed(1)}%  ${passed ? 'PASS' : `FAIL — ${(THRESHOLD - percent).toFixed(1)} points short`}`
  )
}

if (!enforce) {
  console.log('')
  console.log('  Gate is REPORTING ONLY. It becomes enforcing (--enforce) once every')
  console.log('  capability has tests. See openspec/changes/add-test-harness/design.md (D7).')
}
console.log('')

if (enforce && (shortfall || missing)) process.exit(1)
```

- [ ] **Step 3: Write `test.bat`**

Create `test.bat` at the repo root:

```bat
@echo off
setlocal enabledelayedexpansion

set ROOT=%~dp0
set FAILURES=

echo.
echo === Prerequisites ===

powershell -NoProfile -Command "if (-not (Test-NetConnection -ComputerName localhost -Port 55432 -InformationLevel Quiet)) { exit 1 }"
if errorlevel 1 (
  echo   [X] PostgreSQL is not reachable on localhost:55432.
  echo       Backend integration tests need the database published by the test overlay.
  echo       Start it with:
  echo         docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db
  exit /b 1
)
echo   [OK] PostgreSQL on localhost:55432

powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri http://localhost:3000 -TimeoutSec 5 -UseBasicParsing; if ($r.StatusCode -ne 200) { exit 1 } } catch { exit 1 }"
if errorlevel 1 (
  echo   [X] The application is not reachable at http://localhost:3000.
  echo       End-to-end tests need the full stack.
  echo       Start it with:  start.bat
  exit /b 1
)
echo   [OK] Application on http://localhost:3000

echo.
echo === Backend integration ===
rmdir /s /q "%ROOT%BE\Tests.Integration\TestResults" 2>nul
pushd "%ROOT%BE"
dotnet test Tests.Integration\Tests.Integration.csproj ^
  --logger "trx" ^
  --settings Tests.Integration\coverlet.runsettings ^
  --collect:"XPlat Code Coverage"
if errorlevel 1 set FAILURES=!FAILURES! backend
popd

echo.
echo === Frontend unit ===
pushd "%ROOT%FE"
call npm run test:coverage
if errorlevel 1 set FAILURES=!FAILURES! frontend
popd

echo.
echo === End-to-end ===
pushd "%ROOT%FE"
call npm run e2e
if errorlevel 1 set FAILURES=!FAILURES! e2e
popd

echo.
echo === Backend coverage report ===
reportgenerator ^
  -reports:"%ROOT%BE\Tests.Integration\TestResults\**\coverage.cobertura.xml" ^
  -targetdir:"%ROOT%docs\test-coverage\backend" ^
  -reporttypes:"Html;TextSummary"
if errorlevel 1 set FAILURES=!FAILURES! coverage-report

echo.
echo === Line coverage gate ===
rem Add --enforce once every capability has tests (design D7).
node "%ROOT%tools\coverage-gate"
if errorlevel 1 set FAILURES=!FAILURES! coverage-gate

echo.
echo === Scenario coverage ===
node "%ROOT%tools\spec-coverage"
if errorlevel 1 set FAILURES=!FAILURES! spec-coverage

echo.
echo ==========================================================
echo  Summary
echo ==========================================================
if "!FAILURES!"=="" (
  echo   All layers passed.
) else (
  echo   FAILED:!FAILURES!
)
echo.
echo   Backend line coverage : docs\test-coverage\backend\Summary.txt
echo   Frontend line coverage: docs\test-coverage\frontend\index.html
echo   Scenario coverage     : docs\test-coverage\scenarios.md
echo   Playwright report     : FE\playwright-report\
echo.

if not "!FAILURES!"=="" exit /b 1
exit /b 0
```

- [ ] **Step 4: Keep the report directory in git**

```bash
mkdir -p docs/test-coverage
touch docs/test-coverage/.gitkeep
```

- [ ] **Step 5: Run the full pipeline**

```bash
cd c:/Projects/andrey-chat
test.bat
```

Expected: all layers pass; the coverage gate prints both measured percentages with a FAIL and a points-short figure for each (only `user-sessions` is covered, so both will be well under 80%) plus the "REPORTING ONLY" note; the summary lists all four report paths; exit code 0 (spec: "Full run", "Coverage below threshold").

- [ ] **Step 6: Verify the gate can enforce**

```bash
node tools/coverage-gate --enforce
echo "exit code: $?"
```

Expected: exit code 1, with the same measured percentages and shortfalls. This proves the threshold is real and that flipping it later is a one-flag change, not new work.

- [ ] **Step 7: Verify the missing-prerequisite path**

```bash
docker compose stop db
test.bat
```

Expected: exits with "PostgreSQL is not reachable on localhost:55432" and the overlay command, **before any tests run** (spec: "Missing prerequisite").

```bash
docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db
```

- [ ] **Step 8: Verify per-layer runs still work independently**

Run each and confirm each passes alone (spec: "Single layer"):

```bash
cd BE && dotnet test Tests.Integration/Tests.Integration.csproj
cd ../FE && npm test
cd ../FE && npm run e2e
```

The frontend unit run must succeed with the database stopped — verify that too:

```bash
docker compose stop db
cd FE && npm test
docker compose up -d db
```

Expected: passes; the frontend layer has no database prerequisite.

- [ ] **Step 9: Verify repeatability**

Run `test.bat` twice in a row with no cleanup. Expected: identical results both times (spec: "Independent runs"), and no `chat_test_*` database left behind:

```bash
docker compose exec db psql -U postgres -c "SELECT count(*) FROM pg_database WHERE datname LIKE 'chat_test_%'"
```

Expected: `0`.

- [ ] **Step 10: Commit**

```bash
git add test.bat tools/coverage-gate docs/test-coverage/.gitkeep
git commit -m "test: add one-command verification entry point and line-coverage gate"
```

---

### Task 13: Documentation

Deliverable: a developer who has never seen this harness can run it and add a test to it.

**Files:**
- Modify: `CLAUDE.md`
- Modify: `BE/CLAUDE.md`
- Modify: `FE/CLAUDE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Document the pipeline in the root `CLAUDE.md`**

Add a `## Testing` section after the existing "Restart and test the entire project" section:

```markdown
## Testing

`test.bat` runs every gated layer and writes all reports. Prerequisites: the compose
stack up (`start.bat`), plus the database published by the test overlay:

```
docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db
```

The overlay is separate from `docker-compose.yml` on purpose — `platform-constraints`
requires the database not to be reachable from outside the internal network in the
normal deployment topology, so the host port exists only when tests ask for it.

| Layer | Command | Needs |
|---|---|---|
| Backend integration | `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj` | the db overlay above |
| Frontend unit | `cd FE && npm test` | nothing |
| End-to-end | `cd FE && npm run e2e` | the full stack on `localhost:3000` |
| Load and capacity | `dotnet run --project BE/Tests.Load -- <scenario>` | the full stack; run on demand only |
| Scenario report | `node tools/spec-coverage` | results from a previous run |

Reports land in `docs/test-coverage/`: `scenarios.md` (spec coverage), `backend/`
and `frontend/` (line coverage).

**Declaring what a test verifies.** Every test names the spec scenarios it covers by
embedding `@spec:<capability>/<requirement-slug>/<scenario-slug>` in its display name.
`tools/spec-coverage` derives the valid identifiers from `openspec/specs/**/spec.md`,
so a typo or a renamed scenario fails the report rather than silently under-counting.

- Backend: `[Fact(DisplayName = "@spec:user-sessions/... Lists own sessions")]`
- Frontend: `specTest('user-sessions/...', 'lists own sessions', async () => { ... })`
- E2E: `specTest('user-sessions/...', 'revoking another session', async ({ signedInUser }) => { ... })`

**Line coverage gate.** `node tools/coverage-gate` compares measured backend and
frontend line coverage against the 80% threshold and prints how far short each is.
It reports only; pass `--enforce` to make a shortfall exit non-zero. `test.bat` runs
it without `--enforce` because only `user-sessions` has tests so far — add the flag
there once every capability is covered. See
`openspec/changes/add-test-harness/design.md` (D7).
```

- [ ] **Step 2: Document the backend harness in `BE/CLAUDE.md`**

Add:

```markdown
## Integration tests

`BE/Tests.Integration` hosts the Web project in-process with `WebApplicationFactory<Program>`
against a throwaway PostgreSQL database on the compose server (`localhost:55432`).

- Each xUnit collection gets its own `chat_test_{guid}` database, migrated through the
  app's own startup path and dropped afterwards. Databases older than 24 hours are reaped
  on startup, so an aborted run never needs manual cleanup.
- Every test derives from `IntegrationTestBase`, which truncates all tables via Respawn
  before it runs — tests are order-independent and start from empty.
- `AuthHelper.RegisterAndSignInAsync(Fixture)` returns a `TestUser` with a ready `HttpClient`;
  `AuthHelper.RegisterSessionAsync(user, deviceInfo)` adds the `X-Session-Id` header.
- `HubClient.ConnectAsync(Fixture, user)` opens a SignalR connection over the `TestServer`
  handler — no ports. Arm `hub.On<T>("EventName")` **before** the triggering action, then
  await it. Never sleep.
- `Program.cs` ends with `public partial class Program { }` so the factory can find the
  entry point. Do not remove it.

Nothing in the suite connects to the development `chat` database.
```

- [ ] **Step 3: Document the frontend harness in `FE/CLAUDE.md`**

Add:

```markdown
## Tests

**Unit / component** (`npm test`, vitest + jsdom). Helpers live in `src/test/`:

- `renderWithProviders(ui, { route, accessToken })` — QueryClientProvider + MemoryRouter,
  with the auth store seeded. Returns the render result plus `queryClient`.
- `server` (`src/test/server.ts`) — MSW with default handlers for auth, me, and sessions.
  Override per test with `server.use(http.get(...))`. Unhandled requests are an error.
- `createFakeHub()` — stands in for the SignalR client. `hub.emit('MessageReceived', payload)`
  drives a server event synchronously; `hub.invocations` records what the client called.
- `specTest(scenarioId, name, fn)` — declares which spec scenario the test verifies.
- Per-test cleanup (stores, localStorage, sessionStorage, MSW handlers) is automatic
  via `src/test/setup.ts`.

**End-to-end** (`npm run e2e`, Playwright). Targets the running compose stack at
`localhost:3000` — it does not start anything, and fails in global setup with an
actionable message if the stack is down. `e2e/fixtures.ts` provides `signedInUser`
(a freshly registered account) and `openSecondContext(browser, account)` for
two-browser scenarios.
```

- [ ] **Step 4: Verify the docs are accurate**

Follow your own instructions from a clean shell: run each command in the root `CLAUDE.md` table and confirm each behaves as documented.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md BE/CLAUDE.md FE/CLAUDE.md
git commit -m "docs: document the test harness, its prerequisites, and scenario claims"
```

---

## Completion Criteria

Before considering this plan done, verify each of these directly and paste the output:

- [ ] `test.bat` exits 0 and its summary names four report paths
- [ ] `test.bat` run twice in a row produces identical results with no manual cleanup
- [ ] `docker compose exec db psql -U postgres -c "SELECT count(*) FROM pg_database WHERE datname LIKE 'chat_test_%'"` returns 0 after a full run
- [ ] `docs/test-coverage/scenarios.md` reports `user-sessions` at 8/8 and 270 total scenarios
- [ ] A deliberately misspelled scenario claim makes `node tools/spec-coverage` exit 1 and name the stale identifier
- [ ] With `db` stopped, `test.bat` fails on the prerequisite check before running any test
- [ ] With `db` stopped, `cd FE && npm test` still passes
- [ ] `docs/test-coverage/backend/Summary.txt` and `docs/test-coverage/frontend/index.html` both exist and report a line-coverage percentage
- [ ] `node tools/coverage-gate` prints both measured percentages against the 80% threshold with the points-short figure, and `node tools/coverage-gate --enforce` exits 1
- [ ] No `Task.Delay`, `waitForTimeout`, or `sleep` appears in any test file except the single timeout mechanism in `HubClient.AwaitWithTimeout`
- [ ] `git diff main --stat` shows exactly one production-code file changed: `BE/Web/Program.cs`. `docker-compose.yml` is untouched, and `docker compose up -d db` (without the overlay) publishes no host port

Any spec scenario that could not be verified without changing production behavior must appear under `## Findings` in `docs/test-coverage/scenarios.md`.
