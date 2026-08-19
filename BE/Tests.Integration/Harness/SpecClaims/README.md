# Spec claim constants

One class per capability, named `<Capability>Spec`, holding the `@spec:` markers its tests put in
their display names. `tools/spec-coverage` reads those markers out of the `.trx` and fails the run
when one matches no scenario, so a spec rename surfaces here as a named failure rather than as
coverage that quietly disappeared.

Why constants and not a helper method: an attribute argument has to be a compile-time constant.
Concatenating two constants is itself constant, so a marker and a description compose, and a test
covering several scenarios concatenates several markers.

Shape:

```csharp
public static class RoomModerationSpec
{
    private const string Banning = "@spec:room-moderation/banning-users/";

    public const string BanningAMemberWithAReason = Banning + "banning-a-member-with-a-reason ";
}
```

The trailing space matters — the display name is the constant plus a description, and without it
the marker runs into the sentence after it.

`UserSessionsSpec` lives in `Harness/IntegrationTest.cs` instead of here, because it was written
before this folder existed. Moving it would be churn in a file nothing else needs to touch.

The identifier itself is derived mechanically from the spec headings: take the text after
`### Requirement:` and `#### Scenario:`, lowercase it, collapse each run of non-alphanumeric
characters to one `-`, and trim dashes off the ends. See `docs/testing-conventions.md`.

Which capability's scenarios belong at this layer at all is recorded in `docs/test-layer-triage.md`.
