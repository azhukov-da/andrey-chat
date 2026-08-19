namespace Tests.Integration.Harness;

/// <summary>
/// The test collections, and with them the parallelism.
///
/// xUnit runs tests inside a collection sequentially and different collections in parallel, and
/// <see cref="ChatAppFixture"/> is per-collection — so each collection here costs one test database
/// and one in-process host, and buys the ability to run its capability alongside the others.
///
/// Capabilities are grouped rather than given one collection each. Host construction is serialised
/// by <c>ChatAppFactory.BuildGate</c> and each host migrates its own database on the way up, so
/// every extra collection adds fixed start-up cost to the whole run. Grouping keeps that cost
/// proportionate while still letting the seven groups run concurrently. Splitting a group out later
/// is a two-line change here plus the attribute on the test class.
/// </summary>
internal static class Collections
{
    /// <summary>user-accounts, authentication.</summary>
    public const string Identity = "identity";

    /// <summary>chat-rooms.</summary>
    public const string Rooms = "rooms";

    /// <summary>messaging.</summary>
    public const string Messaging = "messaging";

    /// <summary>contacts-and-blocking, direct-messages, room-moderation.</summary>
    public const string Social = "social";

    /// <summary>realtime-delivery, unread-notifications, user-presence.</summary>
    public const string Realtime = "realtime";

    /// <summary>attachments, speech-to-text.</summary>
    public const string Media = "media";

    /// <summary>platform-constraints.</summary>
    public const string Platform = "platform";
}

[CollectionDefinition(Collections.Identity)]
public sealed class IdentityCollection : ICollectionFixture<ChatAppFixture>;

[CollectionDefinition(Collections.Rooms)]
public sealed class RoomsCollection : ICollectionFixture<ChatAppFixture>;

[CollectionDefinition(Collections.Messaging)]
public sealed class MessagingCollection : ICollectionFixture<ChatAppFixture>;

[CollectionDefinition(Collections.Social)]
public sealed class SocialCollection : ICollectionFixture<ChatAppFixture>;

[CollectionDefinition(Collections.Realtime)]
public sealed class RealtimeCollection : ICollectionFixture<ChatAppFixture>;

[CollectionDefinition(Collections.Media)]
public sealed class MediaCollection : ICollectionFixture<ChatAppFixture>;

[CollectionDefinition(Collections.Platform)]
public sealed class PlatformCollection : ICollectionFixture<ChatAppFixture>;
