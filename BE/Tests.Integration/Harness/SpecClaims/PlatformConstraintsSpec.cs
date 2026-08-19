namespace Tests.Integration.Harness;

/// <summary>
/// Scenario claim markers for <c>platform-constraints</c>, mirroring
/// <c>openspec/specs/platform-constraints/spec.md</c>. See <c>Harness/SpecClaims/README.md</c>.
///
/// Two groups of scenarios are deliberately absent. The capacity and very-large-history scenarios
/// belong to the load layer, which is specified but not built. The proxy boundary and the deployment
/// topology are properties of the running compose stack seen from a browser, so they belong to the
/// end-to-end layer. See <c>docs/test-layer-triage.md</c>.
/// </summary>
public static class PlatformConstraintsSpec
{
    private const string Persistence = "@spec:platform-constraints/durable-persistence/";
    private const string FileStorage = "@spec:platform-constraints/file-storage/";
    private const string Consistency = "@spec:platform-constraints/consistency-of-access-decisions/";

    public const string Restart = Persistence + "restart ";
    public const string StartupMigration = Persistence + "startup-migration ";

    public const string ConfiguredRoot = FileStorage + "configured-root ";

    public const string BanTakesEffectImmediately = Consistency + "ban-takes-effect-immediately ";
    public const string PromotionTakesEffectImmediately = Consistency + "promotion-takes-effect-immediately ";
}
