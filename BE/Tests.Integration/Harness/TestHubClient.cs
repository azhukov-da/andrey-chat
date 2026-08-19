using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace Tests.Integration.Harness;

/// <summary>
/// An authenticated SignalR client attached to the in-process host's <c>/hubs/chat</c>.
///
/// Several of these can be connected at once as different users, which is what
/// <c>automated-testing/layered-verification/real-time-behavior-at-the-integration-layer</c>
/// requires: one client causes an event and another asserts it arrived. Everything runs over the
/// <c>TestServer</c>, so there is no listening socket and no port to allocate.
///
/// Event assertions are always backed by a <see cref="TaskCompletionSource"/> with a bounded
/// timeout. There is deliberately no sleep-then-check anywhere in this type — a sleep either
/// makes the suite slow or makes it flaky, usually both.
/// </summary>
public sealed class TestHubClient : IAsyncDisposable
{
    /// <summary>
    /// WebSockets, and not by preference — the hub genuinely depends on it. Hub methods reach the
    /// caller's identity through <c>ICurrentUser</c>, which reads the ambient
    /// <c>IHttpContextAccessor.HttpContext</c>. On WebSockets the connection lives inside one
    /// long-running request, so that context is still ambient when a hub method runs. On long
    /// polling each poll is its own request and the context is gone by then, which makes every
    /// hub method fail as unauthenticated. Testing over long polling would therefore be testing
    /// something the browser never does.
    ///
    /// Set <c>TEST_HUB_TRANSPORT=LongPolling</c> to override, but expect that failure.
    /// </summary>
    private static HttpTransportType PreferredTransport =>
        string.Equals(Environment.GetEnvironmentVariable("TEST_HUB_TRANSPORT"), "LongPolling",
            StringComparison.OrdinalIgnoreCase)
            ? HttpTransportType.LongPolling
            : HttpTransportType.WebSockets;

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<JsonElement>> _received = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TaskCompletionSource<JsonElement>>> _waiters = new();
    private readonly List<IDisposable> _subscriptions = [];

    private TestHubClient(HubConnection connection, TestUser user)
    {
        Connection = connection;
        User = user;
    }

    public HubConnection Connection { get; }

    public TestUser User { get; }

    public static async Task<TestHubClient> ConnectAsync(
        ChatAppFixture fixture,
        TestUser user,
        IEnumerable<string>? subscribeTo = null,
        CancellationToken cancellationToken = default)
    {
        var server = fixture.App.Server;
        var hubUri = new Uri(server.BaseAddress, "hubs/chat");
        var token = user.AccessToken;

        var builder = new HubConnectionBuilder().WithUrl(hubUri, options =>
        {
            options.Transports = PreferredTransport;
            options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            options.AccessTokenProvider = () => Task.FromResult(token);

            if (PreferredTransport == HttpTransportType.WebSockets)
            {
                options.WebSocketFactory = async (context, ct) =>
                {
                    var webSocketClient = server.CreateWebSocketClient();
                    webSocketClient.ConfigureRequest = request =>
                        request.Headers["Authorization"] = $"Bearer {token}";
                    // SignalR hands us a ws:// URI; TestServer's client expects the http:// form.
                    var httpUri = new UriBuilder(context.Uri) { Scheme = "http" }.Uri;
                    return await webSocketClient.ConnectAsync(httpUri, ct);
                };
            }
        });

        var client = new TestHubClient(builder.Build(), user);

        // Subscribe before starting so nothing pushed during connection enrolment is missed.
        foreach (var eventName in subscribeTo ?? ChatHubEvents.All) client.Subscribe(eventName);

        await client.Connection.StartAsync(cancellationToken);

        // StartAsync returns once the handshake is done, which is *before* the server has finished
        // OnConnectedAsync — and that is where the connection is enrolled in its user and room
        // groups, behind a database query. A test that pushed an event in that window would find it
        // delivered to nobody. SignalR does not dispatch a connection's messages until its
        // OnConnectedAsync has completed, so one round trip on this connection is proof that
        // enrolment is done. GetPresenceFor with no ids answers immediately and touches nothing.
        await client.InvokeAsync<Dictionary<string, string>>(
            "GetPresenceFor", (object)Array.Empty<string>());

        return client;
    }

    /// <summary>
    /// Starts recording <paramref name="eventName"/>. Called for every known hub event on connect,
    /// so tests do not have to remember to subscribe.
    /// </summary>
    public void Subscribe(string eventName)
    {
        _received.TryAdd(eventName, new ConcurrentQueue<JsonElement>());
        _waiters.TryAdd(eventName, new ConcurrentQueue<TaskCompletionSource<JsonElement>>());

        _subscriptions.Add(Connection.On<JsonElement>(eventName, payload =>
        {
            // An expectation that timed out cancels its own waiter, so skip past any such waiter:
            // handing this occurrence to a claim nobody is awaiting any more would lose it, and the
            // test that made the next claim would then time out too.
            while (_waiters[eventName].TryDequeue(out var waiter))
            {
                if (waiter.TrySetResult(payload)) return;
            }

            _received[eventName].Enqueue(payload);
        }));
    }

    /// <summary>
    /// Claims the next occurrence of <paramref name="eventName"/> without waiting for it. Call this
    /// <em>before</em> the action that should cause the event, then await the result — that ordering
    /// is what removes the race, and with it the temptation to sleep.
    /// </summary>
    public EventExpectation<T> Expect<T>(string eventName, TimeSpan? timeout = null)
    {
        if (!_received.ContainsKey(eventName)) Subscribe(eventName);

        // An occurrence already sitting in the queue satisfies the expectation immediately.
        if (_received[eventName].TryDequeue(out var buffered))
            return new EventExpectation<T>(eventName, Task.FromResult(buffered), timeout ?? DefaultTimeout);

        var source = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[eventName].Enqueue(source);
        return new EventExpectation<T>(eventName, source.Task, timeout ?? DefaultTimeout, source);
    }

    /// <summary>
    /// Subscribes and waits in one call, for the case where the event is caused by someone else and
    /// there is nothing to interleave.
    /// </summary>
    public Task<T> WaitFor<T>(string eventName, TimeSpan? timeout = null) =>
        Expect<T>(eventName, timeout).ValueAsync();

    /// <summary>Invokes a <c>ChatHub</c> method. Failures surface as <c>HubException</c>.</summary>
    public Task InvokeAsync(string method, params object?[] args) =>
        Connection.InvokeCoreAsync(method, args);

    /// <summary>Invokes a <c>ChatHub</c> method that returns a value.</summary>
    public Task<T> InvokeAsync<T>(string method, params object?[] args) =>
        Connection.InvokeCoreAsync<T>(method, args);

    /// <summary>Occurrences of <paramref name="eventName"/> recorded so far and not yet claimed.</summary>
    public int CountReceived(string eventName) =>
        _received.TryGetValue(eventName, out var queue) ? queue.Count : 0;

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions) subscription.Dispose();
        await Connection.DisposeAsync();
        User.Client.Dispose();
    }
}

/// <summary>A claim on one future hub event, awaited after the action that triggers it.</summary>
public sealed class EventExpectation<T>(
    string eventName,
    Task<JsonElement> payload,
    TimeSpan timeout,
    TaskCompletionSource<JsonElement>? waiter = null)
{
    /// <summary>The raw payload, so a test can assert on fields without a DTO type.</summary>
    public async Task<JsonElement> RawAsync()
    {
        try
        {
            // WaitAsync completes the instant the event arrives; the timeout is a ceiling, not a
            // polling interval, so a passing test never waits for it.
            return await payload.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Abandon();
            throw new TimeoutException(
                $"Hub event '{eventName}' did not arrive within {timeout.TotalSeconds:0.#}s.");
        }
    }

    /// <summary>The payload deserialised into <typeparamref name="T"/>.</summary>
    public async Task<T> ValueAsync()
    {
        var raw = await RawAsync();
        return raw.Deserialize<T>(JsonOptions)!;
    }

    /// <summary>True if the event did not arrive within the timeout. For "nothing was pushed" assertions.</summary>
    public async Task<bool> TimedOutAsync()
    {
        try
        {
            await payload.WaitAsync(timeout);
            return false;
        }
        catch (TimeoutException)
        {
            Abandon();
            return true;
        }
    }

    /// <summary>
    /// Gives up this claim. Cancelling the waiter is what lets the dispatcher recognise it as
    /// abandoned and pass the next occurrence to whoever claimed it afterwards.
    /// </summary>
    private void Abandon() => waiter?.TrySetCanceled();

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
}

/// <summary>
/// The server-to-client hub events, mirroring the table in the root <c>CLAUDE.md</c> and the
/// handler registrations in <c>FE/src/realtime/events.ts</c>. Kept as constants so a rename shows
/// up as a compile error in the tests rather than as a test that silently waits forever.
/// </summary>
public static class ChatHubEvents
{
    public const string MessageReceived = "MessageReceived";
    public const string MessageEdited = "MessageEdited";
    public const string MessageDeleted = "MessageDeleted";
    public const string PresenceChanged = "PresenceChanged";
    public const string RoomMembershipChanged = "RoomMembershipChanged";
    public const string RoomDeleted = "RoomDeleted";
    public const string FriendRequestReceived = "FriendRequestReceived";
    public const string UnreadUpdated = "UnreadUpdated";
    public const string UserTyping = "UserTyping";
    public const string UserStoppedTyping = "UserStoppedTyping";
    public const string AttachmentTranscribed = "AttachmentTranscribed";

    public static readonly string[] All =
    [
        MessageReceived, MessageEdited, MessageDeleted, PresenceChanged, RoomMembershipChanged,
        RoomDeleted, FriendRequestReceived, UnreadUpdated, UserTyping, UserStoppedTyping,
        AttachmentTranscribed,
    ];
}
