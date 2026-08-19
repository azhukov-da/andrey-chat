namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>realtime-delivery</c>, mirroring
/// <c>openspec/specs/realtime-delivery/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// Stopping the connection on sign-out, the reconnect schedule, applying an arriving event to an
/// open view, and reconciling an echoed message by id are all client behaviour and have no
/// constants here. See <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class RealtimeDeliverySpec
{
    private const string Connection = "@spec:realtime-delivery/single-authenticated-realtime-connection/";
    private const string Scoping = "@spec:realtime-delivery/recipient-scoping/";
    private const string Latency = "@spec:realtime-delivery/delivery-latency/";
    private const string Failures = "@spec:realtime-delivery/operation-failures-surface-as-errors/";

    public const string ConnectionAfterSignIn = Connection + "connection-after-sign-in ";
    public const string UnauthenticatedConnect = Connection + "unauthenticated-connect ";

    public const string RoomMessageFanOut = Scoping + "room-message-fan-out ";
    public const string JoiningMidSession = Scoping + "joining-mid-session ";
    public const string PersonalNotification = Scoping + "personal-notification ";
    public const string SubscriptionsOnConnect = Scoping + "subscriptions-on-connect ";

    public const string MessageDelivery = Latency + "message-delivery ";

    public const string RefusedSend = Failures + "refused-send ";
}
