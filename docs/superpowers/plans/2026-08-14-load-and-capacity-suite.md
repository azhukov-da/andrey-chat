# Load and Capacity Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Measure the `platform-constraints` capacity, latency, durability, and topology figures against the real assembled system, at the magnitudes the spec states, and report measured-versus-target.

**Architecture:** A console application (`BE/Tests.Load`) drives the running Docker Compose stack through the same nginx proxy a browser uses. NBomber handles the sustained-load scenario — it establishes a pool of persistent SignalR connections in its init hook and measures send-to-receive latency per step, giving percentiles for free. The one-shot scenarios (large room, large history, restart durability, topology) are measured directly with `Stopwatch`, because they are single measurements rather than load profiles. Two scenarios that are startup checks rather than load checks live in the existing integration project instead, where the host-startup machinery already exists.

**Tech Stack:** .NET 10, NBomber 5, `Microsoft.AspNetCore.SignalR.Client`, `System.Net.Http.Json`; the Docker CLI for the restart and topology checks.

**Spec:** `openspec/changes/add-test-harness/` — the `automated-testing` delta's **"Load and capacity verification"** requirement, and `openspec/specs/platform-constraints/spec.md` for the targets themselves. Design decision D9 covers this suite.

**Depends on:** `docs/superpowers/plans/2026-08-14-test-harness.md` must be complete. This plan uses `tools/spec-coverage` (Task 10–11 there), the `BE/Tests.Integration` harness (Tasks 1–3 there), and `docker-compose.tests.yml` (Task 1 there).

## Global Constraints

- **Targets, copied verbatim from `openspec/specs/platform-constraints/spec.md`:**
  - At least **300** simultaneously connected users.
  - At least **1000** participants in a single chat room.
  - Message delivery to connected recipients within **3 seconds** of the sender's message being accepted.
  - Presence transitions propagate within **2 seconds**.
  - A room holding at least **10,000** messages remains usable to open and scroll.
  - Messages, memberships, roles, room bans, invitations, contacts, blocks, and sessions survive a backend restart.
  - Pending migrations are applied on startup, before serving traffic.
  - The frontend is the only service published to the browser; the database is not exposed outside the internal network.
- **Scenario identifier format and claim marker:** identical to the harness plan — `@spec:<capability>/<requirement-slug>/<scenario-slug>` in the test or scenario display name.
- **This suite is outside the coverage gate.** It is excluded from `test.bat`, from `dotnet test` discovery, and from coverage measurement. (design D9, spec: "Load and capacity verification")
- **Never `sleep` to wait for delivery.** Latency is measured with a `TaskCompletionSource` armed before the send and resolved by the received event; the only timeout is the bounded failure deadline.
- **Report measured figures even when they pass.** A green run that prints no numbers is useless — the point of this suite is the numbers.
- **Exit non-zero when any measured figure misses its target** (spec: "Target not met").
- **Seeding runs against the developer's stack and does not clean up.** Generated names keep runs from colliding; `docker compose down -v` is the reset. Do not attempt selective cleanup — a half-deleted 1000-member room is worse than a stale one.

---

## File Structure

**`BE/Tests.Load/`** — console project, added to `BE.slnx`, excluded from `dotnet test`

| File | Responsibility |
|---|---|
| `Tests.Load.csproj` | Project + packages; `<IsTestProject>false</IsTestProject>` |
| `Program.cs` | CLI dispatch: `dotnet run --project BE/Tests.Load -- <scenario>` |
| `Infrastructure/StackClient.cs` | HTTP + SignalR access to the running stack; registration, rooms, messages |
| `Infrastructure/Measurement.cs` | `Stopwatch` helpers, percentiles, `Result` record, target comparison |
| `Infrastructure/ReportWriter.cs` | Renders measured-vs-target to `docs/test-coverage/load.md` and the console |
| `Scenarios/ConcurrentUsers.cs` | 300 connections; message-delivery and presence-propagation latency |
| `Scenarios/LargeRoom.cs` | 1000 members; posting and member listing |
| `Scenarios/LargeHistory.cs` | 10,000 messages; first-page and older-page load times |
| `Scenarios/RestartDurability.cs` | Write state, restart `be`, verify intact |
| `Scenarios/Topology.cs` | Frontend published, backend and database not |

**`BE/Tests.Integration/PlatformConstraints/`** — startup checks, not load

| File | Responsibility |
|---|---|
| `StartupMigrationTests.cs` | App migrates a behind-schema database before serving traffic |

---

### Task 1: Load project skeleton and stack client

Deliverable: `dotnet run --project BE/Tests.Load -- topology` runs, and the client can register a user, create a room, and post a message against the live stack — proven by the topology scenario, which is the cheapest real scenario and validates the plumbing.

**Files:**
- Create: `BE/Tests.Load/Tests.Load.csproj`
- Create: `BE/Tests.Load/Program.cs`
- Create: `BE/Tests.Load/Infrastructure/StackClient.cs`
- Create: `BE/Tests.Load/Infrastructure/Measurement.cs`
- Create: `BE/Tests.Load/Infrastructure/ReportWriter.cs`
- Create: `BE/Tests.Load/Scenarios/Topology.cs`
- Modify: `BE/BE.slnx`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: nothing from the harness plan except the running stack.
- Produces:
  - `record Measured(string Name, string Target, string Actual, bool Passed, string? Note = null)`
  - `record ScenarioResult(string ScenarioName, string[] ScenarioIds, IReadOnlyList<Measured> Measurements)` with `bool Passed => Measurements.All(m => m.Passed)`
  - `interface ILoadScenario { string Name { get; } Task<ScenarioResult> RunAsync(); }`
  - `StackClient` — `static Task<StackClient> RegisterAsync(string baseUrl = "http://localhost:3000")`, `Task<Guid> CreatePublicRoomAsync(string name)`, `Task JoinRoomAsync(Guid roomId)`, `Task<Guid> SendMessageAsync(Guid roomId, string text)`, `Task<JsonDocument> GetHistoryAsync(Guid roomId, Guid? before, int limit)` (caller disposes), `Task<int> ListMemberCountAsync(Guid roomId)`, `Task<HubConnection> ConnectHubAsync()`, `string UserId { get; }`, `string Username { get; }`, `HttpClient Http { get; }`
  - `Percentiles.Of(IReadOnlyList<double> values)` → `(double P50, double P95, double Max)`
  - `ReportWriter.WriteAsync(IReadOnlyList<ScenarioResult> results)`

- [ ] **Step 1: Create the project**

```bash
cd BE
dotnet new console -n Tests.Load -o Tests.Load
dotnet sln BE.slnx add Tests.Load/Tests.Load.csproj
```

Replace `BE/Tests.Load/Tests.Load.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- Excluded from `dotnet test` discovery and from the coverage gate (design D9). -->
    <IsTestProject>false</IsTestProject>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NBomber" Version="5.8.0" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
  </ItemGroup>

</Project>
```

If NBomber 5.8.0 does not exist, take the latest stable 5.x and note the substitution in the commit message.

- [ ] **Step 2: Verify the project builds and does not appear as a test project**

```bash
cd BE && dotnet build Tests.Load/Tests.Load.csproj
dotnet test --list-tests 2>&1 | grep -i "Tests.Load" || echo "not discovered as a test project — correct"
```

Expected: build succeeds; `Tests.Load` is not discovered.

- [ ] **Step 3: Implement measurement primitives**

Create `BE/Tests.Load/Infrastructure/Measurement.cs`:

```csharp
namespace Tests.Load.Infrastructure;

/// <summary>One measured figure compared against its specified target.</summary>
public record Measured(string Name, string Target, string Actual, bool Passed, string? Note = null);

public record ScenarioResult(string ScenarioName, string[] ScenarioIds, IReadOnlyList<Measured> Measurements)
{
    public bool Passed => Measurements.All(m => m.Passed);
}

public interface ILoadScenario
{
    string Name { get; }
    Task<ScenarioResult> RunAsync();
}

public static class Percentiles
{
    public static (double P50, double P95, double Max) Of(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return (0, 0, 0);
        var sorted = values.OrderBy(v => v).ToArray();
        return (Pick(sorted, 0.50), Pick(sorted, 0.95), sorted[^1]);
    }

    private static double Pick(double[] sorted, double quantile)
    {
        var index = (int)Math.Ceiling(quantile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
```

- [ ] **Step 4: Implement the stack client**

Create `BE/Tests.Load/Infrastructure/StackClient.cs`. Endpoints and request shapes come from `BE/Web/Controllers/` — `POST /api/auth/register` `{email, username, password}`, `POST /api/auth/login` `{email, password}`, `POST /api/Rooms` `{name, description, visibility}`, `POST /api/Rooms/{id}/join`, `POST /api/rooms/{roomId}/Messages` `{text, replyToMessageId}`, `GET /api/rooms/{roomId}/Messages?before=&limit=`.

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Tests.Load.Infrastructure;

public sealed class StackClient
{
    public const string DefaultBaseUrl = "http://localhost:3000";
    private const string Password = "Passw0rd";

    public HttpClient Http { get; }
    public string Username { get; }
    public string UserId { get; private set; } = string.Empty;

    private readonly string _baseUrl;
    private readonly string _accessToken;

    private StackClient(HttpClient http, string baseUrl, string username, string accessToken)
    {
        Http = http;
        _baseUrl = baseUrl;
        Username = username;
        _accessToken = accessToken;
    }

    /// <summary>Registers a brand new account and returns a signed-in client.</summary>
    public static async Task<StackClient> RegisterAsync(string baseUrl = DefaultBaseUrl)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var email = $"load-{suffix}@example.test";
        var username = $"load{suffix}";

        var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(2) };

        var register = await http.PostAsJsonAsync("/api/auth/register",
            new { email, username, password = Password });
        register.EnsureSuccessStatusCode();

        var login = await http.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        using var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = tokens.RootElement.GetProperty("accessToken").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var client = new StackClient(http, baseUrl, username, accessToken);

        var me = await http.GetAsync("/api/Me");
        me.EnsureSuccessStatusCode();
        using var profile = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        client.UserId = profile.RootElement.GetProperty("id").GetString()!;

        return client;
    }

    /// <summary>Public so other load clients can join without an invitation.</summary>
    public async Task<Guid> CreatePublicRoomAsync(string name)
    {
        var response = await Http.PostAsJsonAsync("/api/Rooms",
            new { name, description = "load test room", visibility = 0 });
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    public async Task JoinRoomAsync(Guid roomId)
    {
        var response = await Http.PostAsync($"/api/Rooms/{roomId}/join", content: null);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Guid> SendMessageAsync(Guid roomId, string text)
    {
        var response = await Http.PostAsJsonAsync($"/api/rooms/{roomId}/Messages",
            new { text, replyToMessageId = (Guid?)null });
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    public async Task<JsonDocument> GetHistoryAsync(Guid roomId, Guid? before, int limit)
    {
        var url = $"/api/rooms/{roomId}/Messages?limit={limit}"
                  + (before.HasValue ? $"&before={before.Value}" : string.Empty);
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task<HubConnection> ConnectHubAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl($"{_baseUrl}/hubs/chat", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_accessToken);
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    public async Task<int> ListMemberCountAsync(Guid roomId)
    {
        var response = await Http.GetAsync($"/api/Rooms/{roomId}/members");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetArrayLength();
    }
}
```

Verify each path and property name against the controllers before moving on; if `POST /api/Rooms` returns a different property than `id`, or `RoomVisibility.Public` is not `0`, correct this file — the controllers are authoritative.

- [ ] **Step 5: Implement the report writer**

Create `BE/Tests.Load/Infrastructure/ReportWriter.cs`:

```csharp
using System.Text;

namespace Tests.Load.Infrastructure;

public static class ReportWriter
{
    public static async Task WriteAsync(IReadOnlyList<ScenarioResult> results)
    {
        var repoRoot = FindRepoRoot();
        var path = Path.Combine(repoRoot, "docs", "test-coverage", "load.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var markdown = new StringBuilder();
        markdown.AppendLine("# Load and Capacity Results");
        markdown.AppendLine();
        markdown.AppendLine("Measured against the targets in `openspec/specs/platform-constraints/spec.md`.");
        markdown.AppendLine("Run on demand; not part of the gated verification run.");
        markdown.AppendLine();

        foreach (var result in results)
        {
            markdown.AppendLine($"## {result.ScenarioName} — {(result.Passed ? "PASS" : "FAIL")}");
            markdown.AppendLine();
            foreach (var id in result.ScenarioIds) markdown.AppendLine($"- `@spec:{id}`");
            markdown.AppendLine();
            markdown.AppendLine("| Measurement | Target | Actual | |");
            markdown.AppendLine("|---|---|---|---|");
            foreach (var m in result.Measurements)
            {
                var note = m.Note is null ? string.Empty : $" — {m.Note}";
                markdown.AppendLine($"| {m.Name} | {m.Target} | **{m.Actual}**{note} | {(m.Passed ? "PASS" : "FAIL")} |");
            }
            markdown.AppendLine();
        }

        await File.WriteAllTextAsync(path, markdown.ToString());

        Console.WriteLine();
        foreach (var result in results)
        {
            Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")}  {result.ScenarioName}");
            foreach (var m in result.Measurements)
            {
                Console.WriteLine($"        {m.Name,-40} target {m.Target,-20} actual {m.Actual}");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"Report written to {path}");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
```

- [ ] **Step 6: Implement the topology scenario**

Create `BE/Tests.Load/Scenarios/Topology.cs`. This one is cheap and directly guards the constraint that made the harness use a compose overlay in the first place.

```csharp
using System.Diagnostics;
using Tests.Load.Infrastructure;

namespace Tests.Load.Scenarios;

/// <summary>
/// The frontend is the only service published to the browser; the backend is
/// reachable only through the frontend's proxy, and the database is not exposed
/// outside the internal network.
/// </summary>
public sealed class Topology : ILoadScenario
{
    public string Name => "Deployment topology";

    public async Task<ScenarioResult> RunAsync()
    {
        var measurements = new List<Measured>
        {
            await ReachableAsync("Frontend published on :3000", "http://localhost:3000", expectReachable: true),
            await ReachableAsync("Backend NOT published on :8080", "http://localhost:8080/swagger", expectReachable: false),
            await ProxiedAsync(),
            PortClosed("Database NOT published on :5432", 5432),
        };

        return new ScenarioResult(Name,
            ["platform-constraints/deployment-topology/bringing-the-stack-up"],
            measurements);
    }

    private static async Task<Measured> ReachableAsync(string name, string url, bool expectReachable)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        bool reachable;
        try
        {
            var response = await http.GetAsync(url);
            reachable = response.IsSuccessStatusCode;
        }
        catch
        {
            reachable = false;
        }

        return new Measured(
            name,
            expectReachable ? "reachable" : "not reachable",
            reachable ? "reachable" : "not reachable",
            reachable == expectReachable);
    }

    /// <summary>The backend must be usable through the frontend's proxy path.</summary>
    private static async Task<Measured> ProxiedAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        bool proxied;
        try
        {
            // Unauthenticated: 401 proves the request reached the backend through nginx.
            var response = await http.GetAsync("http://localhost:3000/api/Sessions");
            proxied = response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
        }
        catch
        {
            proxied = false;
        }

        return new Measured(
            "Backend reachable through the frontend proxy",
            "401 via http://localhost:3000/api",
            proxied ? "401 via proxy" : "not proxied",
            proxied);
    }

    private static Measured PortClosed(string name, int port)
    {
        using var probe = new System.Net.Sockets.TcpClient();
        bool open;
        try
        {
            open = probe.ConnectAsync("localhost", port).Wait(TimeSpan.FromSeconds(2)) && probe.Connected;
        }
        catch
        {
            open = false;
        }

        return new Measured(name, "closed", open ? "open" : "closed", !open,
            open ? "a published database port contradicts platform-constraints" : null);
    }
}
```

Note: this checks port 5432, not 55432. The test overlay deliberately publishes 55432, and this scenario is about the base topology, so it must be run against a stack brought up **without** the overlay.

- [ ] **Step 7: Implement the CLI**

Create `BE/Tests.Load/Program.cs`:

```csharp
using Tests.Load.Infrastructure;
using Tests.Load.Scenarios;

var scenarios = new Dictionary<string, Func<ILoadScenario>>(StringComparer.OrdinalIgnoreCase)
{
    ["topology"] = () => new Topology(),
};

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("Load and capacity suite. Run on demand against the running stack.");
    Console.WriteLine();
    Console.WriteLine("  dotnet run --project BE/Tests.Load -- <scenario>");
    Console.WriteLine("  dotnet run --project BE/Tests.Load -- all");
    Console.WriteLine();
    Console.WriteLine("Scenarios: " + string.Join(", ", scenarios.Keys.Order()));
    Console.WriteLine();
    Console.WriteLine("Requires the full stack on http://localhost:3000 (start.bat).");
    return 2;
}

if (!await StackIsUpAsync())
{
    Console.Error.WriteLine("The application stack is not reachable at http://localhost:3000.");
    Console.Error.WriteLine("Start it first:  start.bat   (or: docker compose up -d --build)");
    return 2;
}

var selected = args[0].Equals("all", StringComparison.OrdinalIgnoreCase)
    ? scenarios.Values.Select(factory => factory()).ToList()
    : scenarios.TryGetValue(args[0], out var one)
        ? [one()]
        : null;

if (selected is null)
{
    Console.Error.WriteLine($"Unknown scenario '{args[0]}'. Known: {string.Join(", ", scenarios.Keys.Order())}");
    return 2;
}

var results = new List<ScenarioResult>();
foreach (var scenario in selected)
{
    Console.WriteLine($"Running: {scenario.Name}");
    results.Add(await scenario.RunAsync());
}

await ReportWriter.WriteAsync(results);

return results.All(r => r.Passed) ? 0 : 1;

static async Task<bool> StackIsUpAsync()
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        return (await http.GetAsync("http://localhost:3000")).IsSuccessStatusCode;
    }
    catch
    {
        return false;
    }
}
```

- [ ] **Step 8: Ignore the generated report**

Append to `.gitignore`:

```
/docs/test-coverage/load.md
```

- [ ] **Step 9: Run the topology scenario against a stack with no overlay**

```bash
cd c:/Projects/andrey-chat
docker compose down
start.bat
```

Wait for `http://localhost:3000`, then:

Run: `dotnet run --project BE/Tests.Load -- topology`
Expected: exit 0, four measurements all PASS, `docs/test-coverage/load.md` written.

- [ ] **Step 10: Verify the failure path**

Bring the database up with the test overlay, which publishes 55432 but not 5432, then deliberately publish 5432 as well to confirm the check catches it:

```bash
docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db
dotnet run --project BE/Tests.Load -- topology
```

Expected: still PASS — 5432 is not published, only 55432. This confirms the overlay does not violate the topology constraint.

Then temporarily add `- "5432:5432"` to `docker-compose.tests.yml`, `docker compose -f docker-compose.yml -f docker-compose.tests.yml up -d db`, and re-run.
Expected: exit 1, with "Database NOT published on :5432" FAIL and the note about contradicting `platform-constraints`.

Revert the temporary port and bring the stack back to its normal state.

- [ ] **Step 11: Verify the missing-stack path**

```bash
docker compose stop fe
dotnet run --project BE/Tests.Load -- topology
```

Expected: exit 2, message naming `start.bat`.

```bash
docker compose start fe
```

- [ ] **Step 12: Commit**

```bash
git add BE/Tests.Load BE/BE.slnx .gitignore
git commit -m "test: add load suite skeleton and deployment topology check"
```

---

### Task 2: Concurrent users under load

Deliverable: 300 simultaneous connections, with measured message-delivery and presence-propagation latency compared against the 3s and 2s targets.

**Files:**
- Create: `BE/Tests.Load/Scenarios/ConcurrentUsers.cs`
- Modify: `BE/Tests.Load/Program.cs` (register the scenario)

**Interfaces:**
- Consumes: `StackClient`, `Measured`, `ScenarioResult`, `ILoadScenario`, `Percentiles` (Task 1).
- Produces: nothing consumed by later tasks.

**Scenario identifier claimed:** `platform-constraints/capacity/concurrent-users`

- [ ] **Step 1: Implement the scenario**

Create `BE/Tests.Load/Scenarios/ConcurrentUsers.cs`. NBomber's `WithInit` establishes the connection pool once; each step measures one send-to-receive round trip across an already-connected recipient, so the figure reported is delivery latency, not connection setup.

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;
using NBomber.CSharp;
using Tests.Load.Infrastructure;

namespace Tests.Load.Scenarios;

/// <summary>
/// 300 users connected simultaneously; messaging and presence keep working
/// within the stated latency targets.
/// </summary>
public sealed class ConcurrentUsers : ILoadScenario
{
    private const int UserCount = 300;
    private static readonly TimeSpan DeliveryTarget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PresenceTarget = TimeSpan.FromSeconds(2);

    public string Name => "Concurrent users (300)";

    private readonly List<StackClient> _clients = [];
    private readonly List<HubConnection> _connections = [];
    private readonly ConcurrentBag<double> _deliveryMs = [];
    private readonly ConcurrentBag<double> _presenceMs = [];

    private StackClient _sender = null!;
    private HubConnection _senderConnection = null!;
    private Guid _roomId;

    public async Task<ScenarioResult> RunAsync()
    {
        try
        {
            await SetUpAsync();
            await RunLoadAsync();
            await MeasurePresenceAsync();
        }
        finally
        {
            foreach (var connection in _connections) await connection.DisposeAsync();
            if (_senderConnection is not null) await _senderConnection.DisposeAsync();
        }

        var delivery = Percentiles.Of([.. _deliveryMs]);
        var presence = Percentiles.Of([.. _presenceMs]);

        return new ScenarioResult(Name,
            ["platform-constraints/capacity/concurrent-users"],
            [
                new Measured(
                    "Simultaneous connections established",
                    $"{UserCount}",
                    $"{_connections.Count}",
                    _connections.Count >= UserCount),
                new Measured(
                    "Message delivery p95",
                    $"<= {DeliveryTarget.TotalSeconds}s",
                    $"{delivery.P95 / 1000:0.###}s",
                    delivery.P95 <= DeliveryTarget.TotalMilliseconds,
                    $"p50 {delivery.P50 / 1000:0.###}s, max {delivery.Max / 1000:0.###}s, n={_deliveryMs.Count}"),
                new Measured(
                    "Presence propagation p95",
                    $"<= {PresenceTarget.TotalSeconds}s",
                    $"{presence.P95 / 1000:0.###}s",
                    presence.P95 <= PresenceTarget.TotalMilliseconds,
                    $"p50 {presence.P50 / 1000:0.###}s, max {presence.Max / 1000:0.###}s, n={_presenceMs.Count}"),
            ]);
    }

    private async Task SetUpAsync()
    {
        Console.WriteLine($"  registering {UserCount} users and opening connections...");

        _sender = await StackClient.RegisterAsync();
        _roomId = await _sender.CreatePublicRoomAsync($"load-concurrent-{Guid.NewGuid():N}");
        _senderConnection = await _sender.ConnectHubAsync();

        // Register in bounded batches: 300 sequential registrations is slow,
        // 300 concurrent ones overwhelm Identity's password hashing.
        var batches = Enumerable.Range(0, UserCount).Chunk(20);
        foreach (var batch in batches)
        {
            var created = await Task.WhenAll(batch.Select(async _ =>
            {
                var client = await StackClient.RegisterAsync();
                await client.JoinRoomAsync(_roomId);
                return client;
            }));
            _clients.AddRange(created);
            Console.Write($"\r  users: {_clients.Count}/{UserCount}");
        }
        Console.WriteLine();

        foreach (var chunk in _clients.Chunk(20))
        {
            var connections = await Task.WhenAll(chunk.Select(client => client.ConnectHubAsync()));
            _connections.AddRange(connections);
            Console.Write($"\r  connections: {_connections.Count}/{UserCount}");
        }
        Console.WriteLine();
    }

    private async Task RunLoadAsync()
    {
        // One designated observer measures delivery latency; the other 299
        // connections are the concurrent load the spec asks for.
        var observer = _connections[0];

        var scenario = Scenario.Create("message_delivery", async _ =>
        {
            var marker = Guid.NewGuid().ToString("N");
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var subscription = observer.On<object>("MessageReceived", payload =>
            {
                if (payload?.ToString()?.Contains(marker) == true) received.TrySetResult();
            });

            var stopwatch = Stopwatch.StartNew();
            await _senderConnection.InvokeAsync("SendMessage", _roomId, marker, (Guid?)null);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var completed = await Task.WhenAny(
                received.Task,
                Task.Delay(Timeout.Infinite, timeout.Token));

            stopwatch.Stop();
            if (completed != received.Task) return Response.Fail(message: "delivery timed out");

            timeout.Cancel();
            _deliveryMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            return Response.Ok();
        })
        .WithoutWarmUp()
        .WithLoadSimulations(Simulation.Inject(rate: 5, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));

        NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder(Path.Combine(Path.GetTempPath(), "nbomber", Guid.NewGuid().ToString("N")))
            .Run();
    }

    private async Task MeasurePresenceAsync()
    {
        Console.WriteLine("  measuring presence propagation...");

        var observer = _connections[0];

        for (var i = 0; i < 10; i++)
        {
            var subject = _clients[i + 1];
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            using var subscription = observer.On<object>("PresenceChanged", payload =>
            {
                if (payload?.ToString()?.Contains(subject.UserId) == true) changed.TrySetResult();
            });

            var stopwatch = Stopwatch.StartNew();
            await _connections[i + 1].InvokeAsync("Ping", i % 2 == 0);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var completed = await Task.WhenAny(changed.Task, Task.Delay(Timeout.Infinite, timeout.Token));
            stopwatch.Stop();

            if (completed == changed.Task)
            {
                timeout.Cancel();
                _presenceMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }
    }
}
```

- [ ] **Step 2: Register it in the CLI**

In `BE/Tests.Load/Program.cs`, add to the `scenarios` dictionary:

```csharp
    ["concurrent-users"] = () => new ConcurrentUsers(),
```

- [ ] **Step 3: Run it**

Run: `dotnet run --project BE/Tests.Load -- concurrent-users`
Expected: 300 connections established; three measurements printed with actual figures; `docs/test-coverage/load.md` updated.

This takes several minutes — 300 Identity registrations dominate. That is expected and is why this suite is not in `test.bat`.

- [ ] **Step 4: Record the outcome honestly**

If a target is missed, exit code 1 and the FAIL row are the correct result — do not tune the test to make it pass. Note the measured figures in the commit message so the number is recoverable later.

If presence measurements come back with `n=0`, the `PresenceChanged` payload does not carry the subject's user id in a form the substring check finds. Deserialize the payload into a typed record matching `{ userId, status }` from `FE/src/realtime/events.ts` and match on the property instead.

- [ ] **Step 5: Commit**

```bash
git add BE/Tests.Load/Scenarios/ConcurrentUsers.cs BE/Tests.Load/Program.cs
git commit -m "test: measure delivery and presence latency under 300 concurrent connections"
```

---

### Task 3: Large room

Deliverable: a 1000-member room, with posting and member listing proven to succeed and their timings reported.

**Files:**
- Create: `BE/Tests.Load/Scenarios/LargeRoom.cs`
- Modify: `BE/Tests.Load/Program.cs`

**Interfaces:**
- Consumes: `StackClient`, `Measured`, `ScenarioResult`, `ILoadScenario` (Task 1).
- Produces: nothing consumed by later tasks.

**Scenario identifier claimed:** `platform-constraints/capacity/large-room`

- [ ] **Step 1: Implement the scenario**

Create `BE/Tests.Load/Scenarios/LargeRoom.cs`:

```csharp
using System.Diagnostics;
using Tests.Load.Infrastructure;

namespace Tests.Load.Scenarios;

/// <summary>A room holds 1000 members; posting to it and listing its members succeed.</summary>
public sealed class LargeRoom : ILoadScenario
{
    private const int MemberCount = 1000;

    public string Name => "Large room (1000 members)";

    public async Task<ScenarioResult> RunAsync()
    {
        var owner = await StackClient.RegisterAsync();
        var roomId = await owner.CreatePublicRoomAsync($"load-large-room-{Guid.NewGuid():N}");

        Console.WriteLine($"  provisioning {MemberCount} members (owner counts as one)...");
        var joined = 1;
        foreach (var batch in Enumerable.Range(0, MemberCount - 1).Chunk(20))
        {
            await Task.WhenAll(batch.Select(async _ =>
            {
                var member = await StackClient.RegisterAsync();
                await member.JoinRoomAsync(roomId);
            }));
            joined += batch.Length;
            Console.Write($"\r  members: {joined}/{MemberCount}");
        }
        Console.WriteLine();

        var postStopwatch = Stopwatch.StartNew();
        var messageId = await owner.SendMessageAsync(roomId, $"post to a {MemberCount}-member room");
        postStopwatch.Stop();

        var listStopwatch = Stopwatch.StartNew();
        var memberCount = await owner.ListMemberCountAsync(roomId);
        listStopwatch.Stop();

        return new ScenarioResult(Name,
            ["platform-constraints/capacity/large-room"],
            [
                new Measured("Members in the room", $"{MemberCount}", $"{memberCount}",
                    memberCount >= MemberCount),
                new Measured("Posting succeeds", "succeeds",
                    messageId == Guid.Empty ? "failed" : "succeeded",
                    messageId != Guid.Empty,
                    $"took {postStopwatch.Elapsed.TotalSeconds:0.###}s"),
                new Measured("Listing members succeeds", "succeeds",
                    memberCount > 0 ? "succeeded" : "failed",
                    memberCount > 0,
                    $"took {listStopwatch.Elapsed.TotalSeconds:0.###}s"),
            ]);
    }
}
```

- [ ] **Step 2: Register it in the CLI**

```csharp
    ["large-room"] = () => new LargeRoom(),
```

- [ ] **Step 3: Run it**

Run: `dotnet run --project BE/Tests.Load -- large-room`
Expected: three measurements with actual figures. Provisioning 1000 accounts takes a long time; that is inherent to the target.

If `ListMemberCountAsync` returns fewer than 1000, check whether `GET /api/Rooms/{id}/members` pages its response. If it does, that is a real finding about the endpoint at this scale — record it in the report's note and let the measurement FAIL rather than paging around it, since the spec says listing members succeeds.

- [ ] **Step 4: Commit**

```bash
git add BE/Tests.Load/Scenarios/LargeRoom.cs BE/Tests.Load/Program.cs
git commit -m "test: measure posting and member listing in a 1000-member room"
```

---

### Task 4: Large history

Deliverable: a room seeded past 10,000 messages, with first-page and older-page load times reported.

**Files:**
- Create: `BE/Tests.Load/Scenarios/LargeHistory.cs`
- Modify: `BE/Tests.Load/Program.cs`

**Interfaces:**
- Consumes: `StackClient.GetHistoryAsync`, `Measured`, `ScenarioResult`, `Percentiles` (Task 1).
- Produces: nothing consumed by later tasks.

**Scenario identifier claimed:** `platform-constraints/performance-targets/very-large-history`

- [ ] **Step 1: Implement the scenario**

Create `BE/Tests.Load/Scenarios/LargeHistory.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using Tests.Load.Infrastructure;

namespace Tests.Load.Scenarios;

/// <summary>
/// A room containing more than 10,000 messages: the most recent page renders
/// promptly and scrolling back loads older pages without freezing.
/// </summary>
public sealed class LargeHistory : ILoadScenario
{
    private const int MessageCount = 10_500;
    private const int PageSize = 50;
    private const int PagesToWalk = 20;

    // "Promptly" is not a number in the spec. 2s is the threshold used here,
    // chosen as the point past which a page load reads as a stall. Recorded in
    // the report so the choice is visible rather than implied.
    private static readonly TimeSpan PromptThreshold = TimeSpan.FromSeconds(2);

    public string Name => "Large history (10,000+ messages)";

    public async Task<ScenarioResult> RunAsync()
    {
        var author = await StackClient.RegisterAsync();
        var roomId = await author.CreatePublicRoomAsync($"load-history-{Guid.NewGuid():N}");

        Console.WriteLine($"  seeding {MessageCount} messages...");
        var seeded = 0;
        foreach (var batch in Enumerable.Range(0, MessageCount).Chunk(50))
        {
            await Task.WhenAll(batch.Select(i => author.SendMessageAsync(roomId, $"seeded message {i}")));
            seeded += batch.Length;
            if (seeded % 500 == 0) Console.Write($"\r  messages: {seeded}/{MessageCount}");
        }
        Console.WriteLine($"\r  messages: {seeded}/{MessageCount}");

        var firstPageStopwatch = Stopwatch.StartNew();
        using var firstPage = await author.GetHistoryAsync(roomId, before: null, limit: PageSize);
        firstPageStopwatch.Stop();

        var (items, oldest) = ReadPage(firstPage);
        var olderPageTimes = new List<double>();
        var cursor = oldest;

        for (var page = 0; page < PagesToWalk && cursor.HasValue; page++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var olderPage = await author.GetHistoryAsync(roomId, before: cursor, limit: PageSize);
            stopwatch.Stop();
            olderPageTimes.Add(stopwatch.Elapsed.TotalMilliseconds);

            var (count, next) = ReadPage(olderPage);
            if (count == 0) break;
            cursor = next;
        }

        var older = Percentiles.Of(olderPageTimes);

        return new ScenarioResult(Name,
            ["platform-constraints/performance-targets/very-large-history"],
            [
                new Measured("Messages seeded", $">= 10000", $"{seeded}", seeded >= 10_000),
                new Measured("Most recent page load", $"<= {PromptThreshold.TotalSeconds}s",
                    $"{firstPageStopwatch.Elapsed.TotalSeconds:0.###}s",
                    firstPageStopwatch.Elapsed <= PromptThreshold,
                    $"{items} messages returned"),
                new Measured("Older page load p95", $"<= {PromptThreshold.TotalSeconds}s",
                    $"{older.P95 / 1000:0.###}s",
                    older.P95 <= PromptThreshold.TotalMilliseconds,
                    $"p50 {older.P50 / 1000:0.###}s, max {older.Max / 1000:0.###}s over {olderPageTimes.Count} pages; "
                    + "threshold is this suite's interpretation of \"promptly\", not a spec figure"),
            ]);
    }

    /// <summary>Returns the item count and the id to page before next.</summary>
    private static (int Count, Guid? Oldest) ReadPage(JsonDocument document)
    {
        var root = document.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("items", out var items) ? items : default;

        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0) return (0, null);

        var last = array[array.GetArrayLength() - 1];
        return (array.GetArrayLength(), last.GetProperty("id").GetGuid());
    }
}
```

`ReadPage` handles both a bare array and a `{ items: [...] }` envelope because `MessagesController.GetHistory` returns whatever `IMessageService` produces; check the real shape and simplify this method to the one that actually applies.

- [ ] **Step 2: Register it in the CLI**

```csharp
    ["large-history"] = () => new LargeHistory(),
```

- [ ] **Step 3: Run it**

Run: `dotnet run --project BE/Tests.Load -- large-history`
Expected: three measurements with actual figures. Seeding 10,500 messages over HTTP takes a while.

- [ ] **Step 4: Commit**

```bash
git add BE/Tests.Load/Scenarios/LargeHistory.cs BE/Tests.Load/Program.cs
git commit -m "test: measure history paging in a room with 10,000+ messages"
```

---

### Task 5: Restart durability

Deliverable: state written, backend restarted, state verified intact.

**Files:**
- Create: `BE/Tests.Load/Scenarios/RestartDurability.cs`
- Modify: `BE/Tests.Load/Program.cs`

**Interfaces:**
- Consumes: `StackClient`, `Measured`, `ScenarioResult` (Task 1).
- Produces: nothing consumed by later tasks.

**Scenario identifier claimed:** `platform-constraints/durable-persistence/restart`

- [ ] **Step 1: Implement the scenario**

Create `BE/Tests.Load/Scenarios/RestartDurability.cs`. The spec names messages, memberships, roles, room bans, invitations, contacts, blocks, and sessions; this scenario covers messages, memberships, roles, and bans directly, and records which of the named categories it did not exercise rather than implying full coverage.

```csharp
using System.Diagnostics;
using Tests.Load.Infrastructure;

namespace Tests.Load.Scenarios;

/// <summary>When the backend restarts, prior messages, memberships, and moderation state are intact.</summary>
public sealed class RestartDurability : ILoadScenario
{
    public string Name => "Restart durability";

    public async Task<ScenarioResult> RunAsync()
    {
        var owner = await StackClient.RegisterAsync();
        var member = await StackClient.RegisterAsync();
        var banned = await StackClient.RegisterAsync();

        var roomId = await owner.CreatePublicRoomAsync($"load-restart-{Guid.NewGuid():N}");
        await member.JoinRoomAsync(roomId);
        await banned.JoinRoomAsync(roomId);

        var messageId = await owner.SendMessageAsync(roomId, "written before the restart");

        var promote = await owner.Http.PostAsync($"/api/Rooms/{roomId}/members/{member.UserId}/make-admin", null);
        promote.EnsureSuccessStatusCode();

        var ban = await owner.Http.PostAsJsonAsync($"/api/Rooms/{roomId}/bans/{banned.UserId}",
            new { reason = "restart durability check" });
        ban.EnsureSuccessStatusCode();

        Console.WriteLine("  restarting the backend...");
        var restartStopwatch = Stopwatch.StartNew();
        RunDocker("compose restart be");
        await WaitForStackAsync();
        restartStopwatch.Stop();

        var afterOwner = await StackClient.RegisterAsync();
        using var history = await afterOwner.GetHistoryAsync(roomId, before: null, limit: 50);
        var messageSurvived = history.RootElement.GetRawText().Contains(messageId.ToString());

        var membersAfter = await owner.ListMemberCountAsync(roomId);

        var bansResponse = await owner.Http.GetAsync($"/api/Rooms/{roomId}/bans");
        bansResponse.EnsureSuccessStatusCode();
        var banSurvived = (await bansResponse.Content.ReadAsStringAsync()).Contains(banned.UserId);

        var membersResponse = await owner.Http.GetAsync($"/api/Rooms/{roomId}/members");
        membersResponse.EnsureSuccessStatusCode();
        var membersBody = await membersResponse.Content.ReadAsStringAsync();
        var roleSurvived = membersBody.Contains(member.UserId);

        return new ScenarioResult(Name,
            ["platform-constraints/durable-persistence/restart"],
            [
                new Measured("Backend restarted and became ready", "ready", "ready", true,
                    $"took {restartStopwatch.Elapsed.TotalSeconds:0.#}s"),
                new Measured("Message survived", "intact",
                    messageSurvived ? "intact" : "missing", messageSurvived),
                new Measured("Memberships survived", ">= 2 members",
                    $"{membersAfter}", membersAfter >= 2),
                new Measured("Admin role survived", "intact",
                    roleSurvived ? "intact" : "missing", roleSurvived),
                new Measured("Room ban survived", "intact",
                    banSurvived ? "intact" : "missing", banSurvived,
                    "invitations, contacts, blocks, and sessions are named by the spec but not exercised here"),
            ]);
    }

    private static void RunDocker(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"`docker {arguments}` failed: {process.StandardError.ReadToEnd()}");
        }
    }

    /// <summary>Polls readiness rather than sleeping a fixed interval.</summary>
    private static async Task WaitForStackAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddMinutes(3);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync("http://localhost:3000/api/Sessions");
                // 401 means the backend is up and answering through the proxy.
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) return;
            }
            catch
            {
                // Not up yet.
            }
        }

        throw new TimeoutException("The backend did not become ready within 3 minutes of restarting.");
    }
}
```

Add `using System.Net.Http.Json;` at the top if `PostAsJsonAsync` does not resolve.

- [ ] **Step 2: Register it in the CLI**

```csharp
    ["restart-durability"] = () => new RestartDurability(),
```

- [ ] **Step 3: Run it**

Run: `dotnet run --project BE/Tests.Load -- restart-durability`
Expected: five measurements, all PASS, with the restart duration recorded.

Note that `WaitForStackAsync` spins without a delay between attempts. If that produces excessive noise in the backend logs, add a short `await Task.Delay(500, ...)` between polls — that is a poll interval, not a wait-for-things-to-settle sleep, and is permitted here.

- [ ] **Step 4: Commit**

```bash
git add BE/Tests.Load/Scenarios/RestartDurability.cs BE/Tests.Load/Program.cs
git commit -m "test: verify messages, memberships, roles, and bans survive a backend restart"
```

---

### Task 6: Startup migration

Deliverable: proof that the app applies pending migrations before serving traffic. This is a host-startup check, not a load check, so it lives in `BE/Tests.Integration` where `ChatAppFactory` already exists.

**Files:**
- Create: `BE/Tests.Integration/PlatformConstraints/StartupMigrationTests.cs`

**Interfaces:**
- Consumes: `TestDatabase`, `ChatAppFactory` (harness plan Task 1).
- Produces: nothing consumed by later tasks.

**Scenario identifier claimed:** `platform-constraints/durable-persistence/startup-migration`

- [ ] **Step 1: Write the failing test**

This test manages its own database rather than using the shared collection fixture, because it needs a database that is deliberately *behind* the current schema.

Create `BE/Tests.Integration/PlatformConstraints/StartupMigrationTests.cs`:

```csharp
using FluentAssertions;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Integration.Harness;
using Xunit;

namespace Tests.Integration.PlatformConstraints;

public class StartupMigrationTests
{
    [Fact(DisplayName = "@spec:platform-constraints/durable-persistence/startup-migration Starting against a database behind the current schema applies pending migrations before serving traffic")]
    public async Task Startup_applies_pending_migrations()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var factory = new ChatAppFactory(database.ConnectionString);

        // The database exists but has no schema at all — the furthest-behind
        // version there is. Confirm migrations are pending before the host starts.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pending = await db.Database.GetPendingMigrationsAsync();
            pending.Should().NotBeEmpty("the fresh database has not been migrated yet");
        }

        // Creating a client starts the host, which runs MigrateAsync at startup
        // before the first request is served.
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/Sessions");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized,
            "the host must be serving traffic, not failing on an un-migrated schema");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty(
                "startup applies every pending migration");
            (await db.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
        }
    }
}
```

`ChatAppFactory` must be disposable in an `await using` — `WebApplicationFactory<T>` implements `IAsyncDisposable`, so this works as written.

- [ ] **Step 2: Run it to verify it fails or passes honestly**

Run: `cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --filter "FullyQualifiedName~StartupMigrationTests"`
Expected: 1 passed.

If the first `GetPendingMigrationsAsync` call itself creates the schema, restructure: create the database, assert `GetAppliedMigrationsAsync()` is empty over a raw `NpgsqlConnection` instead of through EF, then start the host.

If the request returns 500 rather than 401, migrations did **not** complete before traffic was served — that is a genuine spec violation, since `Program.cs` swallows migration errors in a `try/catch`. Record it under `## Findings` in `docs/test-coverage/scenarios.md` per the harness plan's Global Constraints; do not change `Program.cs` here.

- [ ] **Step 3: Regenerate the scenario report**

```bash
cd BE && dotnet test Tests.Integration/Tests.Integration.csproj --logger "trx" --settings Tests.Integration/coverlet.runsettings --collect:"XPlat Code Coverage"
cd .. && node tools/spec-coverage
```

Expected: `platform-constraints` now shows 1/10 covered.

- [ ] **Step 4: Commit**

```bash
git add BE/Tests.Integration/PlatformConstraints/StartupMigrationTests.cs
git commit -m "test: verify pending migrations are applied before traffic is served"
```

---

### Task 7: Scenario claims and documentation

Deliverable: the load suite's scenarios appear in the scenario-coverage report, and a developer knows how to run the suite and read its output.

**Files:**
- Modify: `tools/spec-coverage/index.js`
- Create: `tools/spec-coverage/readLoad.js`
- Test: `tools/spec-coverage/readLoad.test.js`
- Modify: `BE/Tests.Load/Infrastructure/ReportWriter.cs`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `readResults.extractScenarioIds`, `buildReport` (harness plan Tasks 10–11); `ScenarioResult` (Task 1).
- Produces: `readLoad(json)` → `Array<Claim>` with `layer: 'load'`

- [ ] **Step 1: Emit machine-readable results from the load suite**

In `BE/Tests.Load/Infrastructure/ReportWriter.cs`, after writing `load.md`, also write a JSON sidecar. Add to `WriteAsync`, before the console output:

```csharp
        var jsonPath = Path.Combine(repoRoot, "docs", "test-coverage", "load-results.json");
        var payload = results.Select(r => new
        {
            scenarioName = r.ScenarioName,
            scenarioIds = r.ScenarioIds,
            passed = r.Passed,
            measurements = r.Measurements.Select(m => new
            {
                name = m.Name, target = m.Target, actual = m.Actual, passed = m.Passed, note = m.Note,
            }),
        });
        await File.WriteAllTextAsync(jsonPath,
            System.Text.Json.JsonSerializer.Serialize(payload,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
```

Add `/docs/test-coverage/load-results.json` to `.gitignore`.

- [ ] **Step 2: Write the failing reader test**

Create `tools/spec-coverage/readLoad.test.js`:

```js
import { strict as assert } from 'node:assert'
import { test } from 'node:test'
import { readLoad } from './readLoad.js'

test('a passing load scenario claims its identifiers', () => {
  const json = [
    {
      scenarioName: 'Large room (1000 members)',
      scenarioIds: ['platform-constraints/capacity/large-room'],
      passed: true,
    },
  ]

  const claims = readLoad(json)

  assert.deepEqual(claims, [
    {
      layer: 'load',
      testName: 'Large room (1000 members)',
      status: 'passed',
      scenarioIds: ['platform-constraints/capacity/large-room'],
    },
  ])
})

test('a failing load scenario does not cover its identifiers', () => {
  const claims = readLoad([
    { scenarioName: 'Concurrent users', scenarioIds: ['platform-constraints/capacity/concurrent-users'], passed: false },
  ])

  assert.equal(claims[0].status, 'failed')
})
```

- [ ] **Step 3: Run it to verify it fails**

Run: `cd tools/spec-coverage && node --test readLoad.test.js`
Expected: FAIL — cannot find module `./readLoad.js`.

- [ ] **Step 4: Implement the reader**

Create `tools/spec-coverage/readLoad.js`:

```js
/**
 * The load suite reports its own results rather than embedding claims in test
 * names, because its scenarios are measurements with targets rather than
 * assertions. A scenario that missed its target does not cover its identifiers.
 */
export function readLoad(json) {
  return (json ?? []).map((scenario) => ({
    layer: 'load',
    testName: scenario.scenarioName ?? '',
    status: scenario.passed ? 'passed' : 'failed',
    scenarioIds: scenario.scenarioIds ?? [],
  }))
}
```

- [ ] **Step 5: Wire it into the entry point**

In `tools/spec-coverage/index.js`, add the import and the read. After the Playwright block:

```js
const LOAD_JSON = join(repoRoot, 'docs', 'test-coverage', 'load-results.json')

const load = await readIfPresent(LOAD_JSON, (text) => readLoad(JSON.parse(text)))
if (load) {
  claims.push(...load)
  layersRun.push('load')
}
```

Add `import { readLoad } from './readLoad.js'` alongside the other imports, and add `LOAD_JSON` next to the other path constants.

- [ ] **Step 6: Run everything and check the report**

```bash
dotnet run --project BE/Tests.Load -- all
node tools/spec-coverage
```

Expected: `layers read` includes `load`; `platform-constraints` shows 5/10 covered (topology, concurrent users, large room, large history, restart) plus 1 from the integration test = 6/10.

The remaining four — `file-storage/configured-root`, `consistency-of-access-decisions/*` (two), and `client-server-transport-boundary/proxied-call` — belong to later per-capability changes at the integration and e2e layers, not to this suite. Confirm they appear in the uncovered list rather than silently disappearing.

- [ ] **Step 7: Document the suite**

In `CLAUDE.md`, expand the `Load and capacity` row of the testing table into its own subsection:

```markdown
### Load and capacity

Run on demand — never part of `test.bat`, and excluded from the coverage gate.

```
dotnet run --project BE/Tests.Load -- all
dotnet run --project BE/Tests.Load -- <topology|concurrent-users|large-room|large-history|restart-durability>
```

Requires the full stack on `http://localhost:3000`. Results — measured figures
against the `platform-constraints` targets — land in `docs/test-coverage/load.md`,
with a JSON sidecar the scenario report reads.

The suite provisions hundreds or thousands of accounts and does not clean up:
generated names avoid collisions, and `docker compose down -v` is the reset.
`concurrent-users` and `large-room` take several minutes each, dominated by
Identity password hashing during registration.

Run `topology` against a stack brought up **without** `docker-compose.tests.yml` —
it asserts the database is not published, which the test overlay deliberately
changes.
```

- [ ] **Step 8: Commit**

```bash
git add tools/spec-coverage/readLoad.js tools/spec-coverage/readLoad.test.js tools/spec-coverage/index.js BE/Tests.Load/Infrastructure/ReportWriter.cs .gitignore CLAUDE.md
git commit -m "test: fold load results into the scenario coverage report"
```

---

## Completion Criteria

Verify each directly and paste the output:

- [ ] `dotnet run --project BE/Tests.Load -- all` runs every scenario and prints actual measured figures for each
- [ ] `docs/test-coverage/load.md` shows a measured-vs-target row for every measurement
- [ ] Exit code is 0 when all targets are met and 1 when any is missed — verify the failure path by temporarily tightening one threshold
- [ ] `dotnet run --project BE/Tests.Load -- topology` exits 2 with a `start.bat` message when the stack is down
- [ ] `dotnet test` at the solution level does not discover or run `Tests.Load`
- [ ] `test.bat` does not invoke the load suite and its runtime is unchanged
- [ ] `node tools/spec-coverage` reports `platform-constraints` at 6/10, with the four uncovered scenarios listed by identifier
- [ ] `docs/test-coverage/backend/Summary.txt` line coverage is unchanged by this plan — the load suite contributes nothing to it
- [ ] Any target that was missed is recorded in `docs/test-coverage/load.md` with its actual figure, not tuned away
