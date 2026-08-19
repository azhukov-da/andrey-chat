namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>user-presence</c>, mirroring
/// <c>openspec/specs/user-presence/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// Deciding to report inactive because a tab is hidden, electing which tab heartbeats, and drawing
/// an indicator are client behaviour and have no constants here. See
/// <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class UserPresenceSpec
{
    private const string States = "@spec:user-presence/presence-states/";
    private const string Heartbeat = "@spec:user-presence/activity-heartbeat-and-idle-threshold/";
    private const string MultiTab = "@spec:user-presence/multi-tab-presence/";
    private const string Broadcast = "@spec:user-presence/presence-change-broadcast/";
    private const string Lookup = "@spec:user-presence/presence-lookup-on-demand/";

    public const string ConnectedAndActive = States + "connected-and-active ";
    public const string ConnectedButIdle = States + "connected-but-idle ";
    public const string NoConnection = States + "no-connection ";

    public const string GoingIdle = Heartbeat + "going-idle ";
    public const string ReturningFromIdle = Heartbeat + "returning-from-idle ";

    public const string TwoTabsOpen = MultiTab + "two-tabs-open ";
    public const string ClosingTheLastTab = MultiTab + "closing-the-last-tab ";

    public const string ContactComesOnline = Broadcast + "contact-comes-online ";
    public const string ContactDisconnects = Broadcast + "contact-disconnects ";

    public const string HydratingAFreshlyOpenedView = Lookup + "hydrating-a-freshly-opened-view ";
    public const string AfterReconnect = Lookup + "after-reconnect ";
}
